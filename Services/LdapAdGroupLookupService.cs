using AdGroupUserCompare.Models;
using AdGroupUserCompare.Options;
using Novell.Directory.Ldap;

namespace AdGroupUserCompare.Services;

public sealed class LdapAdGroupLookupService(LdapSettingsStore settings, ILogger<LdapAdGroupLookupService> logger)
    : IAdGroupLookupService
{
    private readonly LdapOptions _options = settings.Current;

    public async Task<AdSearchResponse> SearchAsync(AdSearchRequest request, CancellationToken cancellationToken)
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
        var endpoint = LdapEndpointResolver.Resolve(server, _options.Port, _options.UseSsl, _options.UseStartTls);
        using var connection = new ManagedLdapClient(endpoint, _options.VerifyCertificate, TimeSpan.FromSeconds(45));
        await connection.ConnectAsync(endpoint, cancellationToken);
        if (endpoint.UseStartTls)
        {
            await connection.StartTlsAsync(cancellationToken);
        }

        await connection.BindAsync(_options.BindDn, _options.BindPassword, cancellationToken);
        var groups = (await FindGroupsAsync(connection, searchBase, request.GroupPattern, cancellationToken))
            .OrderBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var results = new List<AdUserResult>();
        var seenUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            await ResolveGroupMembersAsync(
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

        return new AdSearchResponse(
            results,
            groups.Select(group => group.Name).ToList(),
            groups.Count,
            userCount);
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

    private async Task<List<GroupEntry>> FindGroupsAsync(
        ManagedLdapClient connection,
        string searchBase,
        string groupPattern,
        CancellationToken cancellationToken)
    {
        var filter = $"(&(objectClass=group)(cn={EscapeLdapFilterValue(groupPattern)}))";
        var entries = await connection.SearchAsync(
            searchBase,
            LdapConnection.ScopeSub,
            filter,
            ["distinguishedName", "cn", "name"],
            _options.UsePaging,
            _options.PageSize,
            cancellationToken);

        return entries
            .Select(entry => new GroupEntry(
                FirstNonEmpty(GetString(entry, "distinguishedName"), entry.Dn),
                FirstNonEmpty(GetString(entry, "cn"), GetString(entry, "name"))))
            .Where(group => !string.IsNullOrWhiteSpace(group.DistinguishedName) && !string.IsNullOrWhiteSpace(group.Name))
            .ToList();
    }

    private async Task ResolveGroupMembersAsync(
        ManagedLdapClient connection,
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

        foreach (var memberDn in await GetMemberDnsAsync(connection, groupDn, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var member = await LoadDirectoryEntryAsync(connection, memberDn, cancellationToken);
            if (member is null)
            {
                logger.LogWarning("LDAP member not found: {MemberDn}", memberDn);
                continue;
            }

            if (HasObjectClass(member, "group"))
            {
                var nestedName = FirstNonEmpty(GetString(member, "cn"), GetString(member, "name"));
                await ResolveGroupMembersAsync(
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
                FirstNonEmpty(GetString(member, "distinguishedName"), member.Dn)));
        }
    }

    private static async Task<LdapEntry?> LoadDirectoryEntryAsync(
        ManagedLdapClient connection,
        string distinguishedName,
        CancellationToken cancellationToken)
    {
        var entries = await connection.SearchAsync(
            distinguishedName,
            LdapConnection.ScopeBase,
            "(objectClass=*)",
            ["objectClass", "distinguishedName", "cn", "name", "sAMAccountName", "displayName", "mail", "userAccountControl", "department", "title"],
            usePaging: false,
            pageSize: 1,
            cancellationToken);
        return entries.FirstOrDefault();
    }

    private async Task<IReadOnlyList<string>> GetMemberDnsAsync(
        ManagedLdapClient connection,
        string groupDn,
        CancellationToken cancellationToken)
    {
        var members = new List<string>();
        var rangeSize = Math.Max(1, _options.MemberRangeSize);
        var rangeStart = 0;

        while (true)
        {
            var rangeEnd = rangeStart + rangeSize - 1;
            var attributeName = $"member;range={rangeStart}-{rangeEnd}";
            var entries = await connection.SearchAsync(
                groupDn,
                LdapConnection.ScopeBase,
                "(objectClass=group)",
                ["member", attributeName],
                usePaging: false,
                pageSize: 1,
                cancellationToken);
            var entry = entries.FirstOrDefault();
            if (entry is null)
            {
                return members;
            }

            if (entry.Contains("member"))
            {
                AddAttributeValues(entry.Get("member"), members);
                return members;
            }

            var rangedAttribute = entry.GetAttributeSet()
                .Select(attribute => attribute.Value)
                .FirstOrDefault(attribute => attribute.Name.StartsWith("member;range=", StringComparison.OrdinalIgnoreCase));
            if (rangedAttribute is null)
            {
                return members;
            }

            AddAttributeValues(rangedAttribute, members);
            if (rangedAttribute.Name.EndsWith("-*", StringComparison.Ordinal))
            {
                return members;
            }

            rangeStart += rangeSize;
        }
    }

    private static void AddAttributeValues(LdapAttribute attribute, List<string> values)
    {
        foreach (var value in attribute.StringValueArray)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value);
            }
        }
    }

    private static bool HasObjectClass(LdapEntry entry, string objectClass)
    {
        return entry.Contains("objectClass") && entry.Get("objectClass").StringValueArray
            .Any(value => string.Equals(value, objectClass, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsUserEnabled(LdapEntry entry)
    {
        var rawValue = GetString(entry, "userAccountControl");
        return !int.TryParse(rawValue, out var userAccountControl) || (userAccountControl & 0x2) == 0;
    }

    private static string GetString(LdapEntry entry, string attributeName)
    {
        return entry.Contains(attributeName) ? entry.Get(attributeName).StringValue ?? "" : "";
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
