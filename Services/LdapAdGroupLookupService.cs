using System.DirectoryServices.Protocols;
using System.Net;
using AdGroupUserCompare.Models;
using AdGroupUserCompare.Options;

namespace AdGroupUserCompare.Services;

public sealed class LdapAdGroupLookupService(LdapSettingsStore settings, ILogger<LdapAdGroupLookupService> logger)
    : IAdGroupLookupService
{
    private readonly LdapOptions _options = settings.Current;

    public Task<AdSearchResponse> SearchAsync(AdSearchRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.GroupPattern))
        {
            throw new InvalidOperationException("Gruppenmuster fehlt.");
        }

        var server = FirstNonEmpty(request.Server, _options.Server);
        var searchBase = FirstNonEmpty(request.SearchBase, _options.SearchBase);

        if (string.IsNullOrWhiteSpace(server))
        {
            throw new InvalidOperationException("LDAP-Server fehlt. Setze Ad__Server oder AD_LDAP_SERVER.");
        }

        if (string.IsNullOrWhiteSpace(searchBase))
        {
            throw new InvalidOperationException("SearchBase fehlt. Setze Ad__SearchBase oder AD_SEARCH_BASE.");
        }

        ValidateBindConfiguration();
        ValidateTransportConfiguration();

        using var connection = CreateConnection(server);
        var groups = FindGroups(connection, searchBase, request.GroupPattern, cancellationToken)
            .OrderBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var results = new List<AdUserResult>();
        var seenUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResolveGroupMembers(
                connection,
                rootGroupName: group.Name,
                groupDn: group.DistinguishedName,
                path: [group.Name],
                visitedGroups: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                seenUsers: seenUsers,
                results: results,
                onlyEnabled: request.OnlyEnabled,
                cancellationToken: cancellationToken);
        }

        var userCount = results
            .Select(result => result.DistinguishedName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return Task.FromResult(new AdSearchResponse(
            results,
            groups.Select(group => group.Name).ToList(),
            groups.Count,
            userCount));
    }

    private LdapConnection CreateConnection(string server)
    {
        NativeLdapTlsOptions.Apply(_options);

        var identifier = new LdapDirectoryIdentifier(server, _options.Port, fullyQualifiedDnsHostName: false, connectionless: false);
        var connection = new LdapConnection(identifier)
        {
            AuthType = string.IsNullOrWhiteSpace(_options.BindDn) ? AuthType.Anonymous : AuthType.Basic,
            Timeout = TimeSpan.FromSeconds(45)
        };

        connection.SessionOptions.ProtocolVersion = 3;
        ConfigureCertificateValidation(connection);
        connection.SessionOptions.SecureSocketLayer = _options.UseSsl;
        connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
        StartTransportLayerSecurity(connection);

        var credential = CreateCredential();
        connection.Credential = credential;
        BindConnection(connection, credential);
        return connection;
    }

    private void BindConnection(LdapConnection connection, NetworkCredential? credential)
    {
        if (credential is null)
        {
            connection.Bind();
            return;
        }

        connection.Bind(credential);
    }

    private NetworkCredential? CreateCredential()
    {
        return string.IsNullOrWhiteSpace(_options.BindDn)
            ? null
            : new NetworkCredential(_options.BindDn, _options.BindPassword);
    }

    private void StartTransportLayerSecurity(LdapConnection connection)
    {
        if (_options.UseStartTls)
        {
            connection.SessionOptions.StartTransportLayerSecurity(new DirectoryControlCollection());
        }
    }

    private void ConfigureCertificateValidation(LdapConnection connection)
    {
        if (!_options.VerifyCertificate)
        {
            connection.SessionOptions.VerifyServerCertificate = (_, _) => true;
        }
    }

    private void ValidateBindConfiguration()
    {
        if (!string.IsNullOrWhiteSpace(_options.BindDn) && string.IsNullOrEmpty(_options.BindPassword))
        {
            throw new InvalidOperationException("AD_BIND_DN ist gesetzt, aber AD_BIND_PASSWORD ist leer oder kommt nicht im Container an.");
        }

        if (string.IsNullOrWhiteSpace(_options.BindDn) && !string.IsNullOrEmpty(_options.BindPassword))
        {
            throw new InvalidOperationException("AD_BIND_PASSWORD ist gesetzt, aber AD_BIND_DN fehlt. Das Passwort wuerde sonst ignoriert.");
        }
    }

    private void ValidateTransportConfiguration()
    {
        if (_options.UseSsl && _options.UseStartTls)
        {
            throw new InvalidOperationException("AD_USE_SSL und AD_USE_START_TLS duerfen nicht gleichzeitig aktiv sein. Nutze entweder LDAPS auf Port 636 oder StartTLS auf Port 389.");
        }

        if (_options.UseSsl && _options.Port == 389)
        {
            throw new InvalidOperationException("LDAPS ist aktiv, aber Port 389 ist gesetzt. Nutze fuer LDAPS Port 636 oder fuer Port 389 StartTLS.");
        }

        if (_options.UseStartTls && _options.Port == 636)
        {
            throw new InvalidOperationException("StartTLS ist aktiv, aber Port 636 ist gesetzt. Nutze fuer StartTLS Port 389 oder fuer Port 636 LDAPS.");
        }
    }

    private List<GroupEntry> FindGroups(LdapConnection connection, string searchBase, string groupPattern, CancellationToken cancellationToken)
    {
        var ldapPattern = EscapeLdapFilterValue(groupPattern);
        var filter = $"(&(objectClass=group)(name={ldapPattern}))";
        var request = new SearchRequest(searchBase, filter, SearchScope.Subtree, "distinguishedName", "name");

        return ExecutePagedSearch(connection, request, cancellationToken)
            .Select(entry => new GroupEntry(
                GetString(entry, "distinguishedName"),
                GetString(entry, "name")))
            .Where(group => !string.IsNullOrWhiteSpace(group.DistinguishedName) && !string.IsNullOrWhiteSpace(group.Name))
            .ToList();
    }

    private void ResolveGroupMembers(
        LdapConnection connection,
        string rootGroupName,
        string groupDn,
        IReadOnlyList<string> path,
        HashSet<string> visitedGroups,
        HashSet<string> seenUsers,
        List<AdUserResult> results,
        bool onlyEnabled,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!visitedGroups.Add(groupDn))
        {
            return;
        }

        foreach (var memberDn in GetMemberDns(connection, groupDn))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var member = LoadDirectoryEntry(connection, memberDn);
            if (member is null)
            {
                logger.LogWarning("LDAP member not found: {MemberDn}", memberDn);
                continue;
            }

            if (HasObjectClass(member, "group"))
            {
                var nestedName = GetString(member, "name");
                ResolveGroupMembers(
                    connection,
                    rootGroupName,
                    memberDn,
                    [.. path, string.IsNullOrWhiteSpace(nestedName) ? memberDn : nestedName],
                    visitedGroups,
                    seenUsers,
                    results,
                    onlyEnabled,
                    cancellationToken);
                continue;
            }

            if (!HasObjectClass(member, "user"))
            {
                continue;
            }

            var enabled = IsUserEnabled(member);
            if (onlyEnabled && !enabled)
            {
                continue;
            }

            var key = $"{groupDn}|{memberDn}";
            if (!seenUsers.Add(key))
            {
                continue;
            }

            results.Add(new AdUserResult(
                rootGroupName,
                string.Join(" > ", path),
                GetString(member, "sAMAccountName"),
                GetString(member, "displayName"),
                GetString(member, "mail"),
                enabled,
                GetString(member, "department"),
                GetString(member, "title"),
                GetString(member, "distinguishedName")));
        }
    }

    private SearchResultEntry? LoadDirectoryEntry(LdapConnection connection, string distinguishedName)
    {
        var request = new SearchRequest(
            distinguishedName,
            "(objectClass=*)",
            SearchScope.Base,
            "objectClass",
            "distinguishedName",
            "name",
            "sAMAccountName",
            "displayName",
            "mail",
            "userAccountControl",
            "department",
            "title");

        var response = (SearchResponse)connection.SendRequest(request);
        return response.Entries.Count == 0 ? null : response.Entries[0];
    }

    private IReadOnlyList<string> GetMemberDns(LdapConnection connection, string groupDn)
    {
        var members = new List<string>();
        var rangeSize = Math.Max(1, _options.MemberRangeSize);
        var rangeStart = 0;

        while (true)
        {
            var rangeEnd = rangeStart + rangeSize - 1;
            var attributeName = $"member;range={rangeStart}-{rangeEnd}";
            var request = new SearchRequest(groupDn, "(objectClass=group)", SearchScope.Base, "member", attributeName);
            var response = (SearchResponse)connection.SendRequest(request);

            if (response.Entries.Count == 0)
            {
                return members;
            }

            var attributes = response.Entries[0].Attributes;
            if (attributes.Contains("member"))
            {
                AddAttributeValues(attributes["member"], members);
                return members;
            }

            var rangedAttributeName = attributes.AttributeNames
                .Cast<string>()
                .FirstOrDefault(name => name.StartsWith("member;range=", StringComparison.OrdinalIgnoreCase));

            if (rangedAttributeName is null)
            {
                return members;
            }

            AddAttributeValues(attributes[rangedAttributeName], members);
            if (rangedAttributeName.EndsWith("-*", StringComparison.Ordinal))
            {
                return members;
            }

            rangeStart += rangeSize;
        }
    }

    private List<SearchResultEntry> ExecutePagedSearch(LdapConnection connection, SearchRequest request, CancellationToken cancellationToken)
    {
        var results = new List<SearchResultEntry>();

        if (!_options.UsePaging)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = (SearchResponse)connection.SendRequest(request);
            results.AddRange(response.Entries.Cast<SearchResultEntry>());
            return results;
        }

        var pageSize = Math.Max(1, _options.PageSize);
        var pageControl = new PageResultRequestControl(pageSize);
        request.Controls.Add(pageControl);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = (SearchResponse)connection.SendRequest(request);
            results.AddRange(response.Entries.Cast<SearchResultEntry>());

            var responseControl = response.Controls
                .OfType<PageResultResponseControl>()
                .FirstOrDefault();

            if (responseControl is null || responseControl.Cookie.Length == 0)
            {
                break;
            }

            pageControl.Cookie = responseControl.Cookie;
        }

        return results;
    }

    private static void AddAttributeValues(DirectoryAttribute attribute, List<string> values)
    {
        foreach (var value in attribute.GetValues(typeof(string)).Cast<string>())
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value);
            }
        }
    }

    private static bool HasObjectClass(SearchResultEntry entry, string objectClass)
    {
        if (!entry.Attributes.Contains("objectClass"))
        {
            return false;
        }

        return entry.Attributes["objectClass"]
            .GetValues(typeof(string))
            .Cast<string>()
            .Any(value => string.Equals(value, objectClass, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsUserEnabled(SearchResultEntry entry)
    {
        var rawValue = GetString(entry, "userAccountControl");
        return !int.TryParse(rawValue, out var userAccountControl) || (userAccountControl & 0x2) == 0;
    }

    private static string GetString(SearchResultEntry entry, string attributeName)
    {
        if (!entry.Attributes.Contains(attributeName) || entry.Attributes[attributeName].Count == 0)
        {
            return "";
        }

        return entry.Attributes[attributeName][0]?.ToString() ?? "";
    }

    private static string EscapeLdapFilterValue(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var character in value)
        {
            switch (character)
            {
                case '(':
                    builder.Append(@"\28");
                    break;
                case ')':
                    builder.Append(@"\29");
                    break;
                case '\\':
                    builder.Append(@"\5c");
                    break;
                case '\0':
                    builder.Append(@"\00");
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        return builder.ToString();
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
    }

    private sealed record GroupEntry(string DistinguishedName, string Name);
}
