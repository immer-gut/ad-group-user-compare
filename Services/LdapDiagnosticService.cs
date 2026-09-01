using System.DirectoryServices.Protocols;
using System.Net;
using AdGroupUserCompare.Models;
using AdGroupUserCompare.Options;
using Microsoft.Extensions.Options;

namespace AdGroupUserCompare.Services;

public sealed class LdapDiagnosticService(IOptions<LdapOptions> options) : ILdapDiagnosticService
{
    private readonly LdapOptions _options = options.Value;

    public Task<LdapTestResponse> TestAsync(LdapTestRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var server = FirstNonEmpty(request.Server, _options.Server);
        var searchBase = FirstNonEmpty(request.SearchBase, _options.SearchBase);
        var steps = new List<LdapTestStep>();

        if (string.IsNullOrWhiteSpace(server))
        {
            steps.Add(new LdapTestStep("Konfiguration", false, "LDAP-Server fehlt.", "Setze AD_LDAP_SERVER oder Ad__Server im Portainer Stack."));
            return Task.FromResult(BuildResponse(false, server, searchBase, steps));
        }

        if (string.IsNullOrWhiteSpace(searchBase))
        {
            steps.Add(new LdapTestStep("Konfiguration", false, "SearchBase fehlt.", "Setze AD_SEARCH_BASE oder Ad__SearchBase, z. B. DC=example,DC=local."));
            return Task.FromResult(BuildResponse(false, server, searchBase, steps));
        }

        if (!ValidateBindConfiguration(steps))
        {
            return Task.FromResult(BuildResponse(false, server, searchBase, steps));
        }

        if (!ValidateTransportConfiguration(steps))
        {
            return Task.FromResult(BuildResponse(false, server, searchBase, steps));
        }

        steps.Add(new LdapTestStep(
            "Konfiguration",
            true,
            "Pflichtwerte vorhanden.",
            $"{ProtocolName()} {server}:{_options.Port}, SearchBase {searchBase}, Bind-DN {BindDnLabel()}, Passwort {BindPasswordLabel()}, Paging {PagingLabel()}, Referrals aus"));

        LdapConnection? connection = null;
        if (!TryStep(
            "Verbindung",
            steps,
            () =>
            {
                connection = CreateConnection(server);
                return "LDAP-Client wurde initialisiert.";
            },
            "Verbindungsaufbau vorbereitet."))
        {
            return Task.FromResult(BuildResponse(false, server, searchBase, steps));
        }

        var activeConnection = connection ?? throw new InvalidOperationException("LDAP-Verbindung wurde nicht initialisiert.");
        using (activeConnection)
        {
            if (_options.UseStartTls &&
                !TryStep("StartTLS", steps, () => StartTransportLayerSecurity(activeConnection), "StartTLS erfolgreich."))
            {
                return Task.FromResult(BuildResponse(false, server, searchBase, steps));
            }

            if (!TryStep("Bind", steps, () => BindConnection(activeConnection), "LDAP-Bind erfolgreich."))
            {
                return Task.FromResult(BuildResponse(false, server, searchBase, steps));
            }

            if (!TryStep(
                "SearchBase",
                steps,
                () => ProbeSearchBase(activeConnection, searchBase),
                "SearchBase ist lesbar."))
            {
                return Task.FromResult(BuildResponse(false, server, searchBase, steps));
            }

            var groupPattern = request.GroupPattern?.Trim();
            if (!string.IsNullOrWhiteSpace(groupPattern))
            {
                if (!TryStep(
                    "Gruppenmuster ohne Paging",
                    steps,
                    () => ProbeGroupPattern(activeConnection, searchBase, groupPattern, usePaging: false),
                    "Gruppensuche ohne Paging erfolgreich."))
                {
                    return Task.FromResult(BuildResponse(false, server, searchBase, steps));
                }

                if (_options.UsePaging)
                {
                    TryStep(
                        "Gruppenmuster mit Paging",
                        steps,
                        () => ProbeGroupPattern(activeConnection, searchBase, groupPattern, usePaging: true),
                        "Gruppensuche mit Paging erfolgreich.");
                }
                else
                {
                    steps.Add(new LdapTestStep(
                        "Gruppenmuster mit Paging",
                        true,
                        "Uebersprungen.",
                        "AD_USE_PAGING=false ist gesetzt. Die App nutzt Gruppensuche ohne PageResult-Control."));
                }
            }
            else
            {
                steps.Add(new LdapTestStep("Gruppenmuster", true, "Uebersprungen.", "Trage ein Gruppenmuster ein, um auch die Gruppensuche zu testen."));
            }
        }

        return Task.FromResult(BuildResponse(steps.All(step => step.Success), server, searchBase, steps));
    }

    private LdapConnection CreateConnection(string server)
    {
        var identifier = new LdapDirectoryIdentifier(server, _options.Port, fullyQualifiedDnsHostName: false, connectionless: false);
        var connection = new LdapConnection(identifier)
        {
            AuthType = string.IsNullOrWhiteSpace(_options.BindDn) ? AuthType.Anonymous : AuthType.Basic,
            Credential = CreateCredential(),
            Timeout = TimeSpan.FromSeconds(20)
        };

        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.SecureSocketLayer = _options.UseSsl;
        connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
        return connection;
    }

    private void StartTransportLayerSecurity(LdapConnection connection)
    {
        connection.SessionOptions.StartTransportLayerSecurity(new DirectoryControlCollection());
    }

    private string BindConnection(LdapConnection connection)
    {
        var credential = CreateCredential();
        if (credential is null)
        {
            connection.Bind();
            return "Anonymer LDAP-Bind wurde ausgefuehrt.";
        }

        connection.Bind(credential);
        return $"Expliziter LDAP-Bind mit {BindDnLabel()} wurde ausgefuehrt.";
    }

    private NetworkCredential? CreateCredential()
    {
        return string.IsNullOrWhiteSpace(_options.BindDn)
            ? null
            : new NetworkCredential(_options.BindDn, _options.BindPassword);
    }

    private static void ProbeSearchBase(LdapConnection connection, string searchBase)
    {
        var request = new SearchRequest(searchBase, "(objectClass=*)", SearchScope.Base, "distinguishedName", "name");
        request.TimeLimit = TimeSpan.FromSeconds(15);
        connection.SendRequest(request);
    }

    private static string ProbeGroupPattern(LdapConnection connection, string searchBase, string groupPattern, bool usePaging)
    {
        var request = new SearchRequest(
            searchBase,
            $"(&(objectClass=group)(name={EscapeLdapFilterValue(groupPattern)}))",
            SearchScope.Subtree,
            "distinguishedName",
            "name");
        request.TimeLimit = TimeSpan.FromSeconds(20);
        if (usePaging)
        {
            request.Controls.Add(new PageResultRequestControl(10));
        }

        var response = (SearchResponse)connection.SendRequest(request);
        if (!usePaging)
        {
            return $"{response.Entries.Count} Gruppe(n) in der Testabfrage gefunden.";
        }

        var hasMore = response.Controls.OfType<PageResultResponseControl>().Any(control => control.Cookie.Length > 0);
        return hasMore
            ? $"Mindestens {response.Entries.Count} Gruppe(n) in der Testabfrage gefunden."
            : $"{response.Entries.Count} Gruppe(n) in der Testabfrage gefunden.";
    }

    private static bool TryStep(string name, List<LdapTestStep> steps, Action action, string successMessage)
    {
        try
        {
            action();
            steps.Add(new LdapTestStep(name, true, successMessage, null));
            return true;
        }
        catch (Exception ex)
        {
            steps.Add(new LdapTestStep(name, false, FriendlyMessage(ex), DiagnosticDetail(ex)));
            return false;
        }
    }

    private static bool TryStep(string name, List<LdapTestStep> steps, Func<string> action, string successMessage)
    {
        try
        {
            var detail = action();
            steps.Add(new LdapTestStep(name, true, successMessage, detail));
            return true;
        }
        catch (Exception ex)
        {
            steps.Add(new LdapTestStep(name, false, FriendlyMessage(ex), DiagnosticDetail(ex)));
            return false;
        }
    }

    private LdapTestResponse BuildResponse(bool success, string server, string searchBase, IReadOnlyList<LdapTestStep> steps)
    {
        return new LdapTestResponse(
            success,
            server,
            _options.Port,
            _options.UseSsl,
            _options.UseStartTls,
            searchBase,
            !string.IsNullOrWhiteSpace(_options.BindDn),
            _options.BindDn,
            !string.IsNullOrEmpty(_options.BindPassword),
            steps);
    }

    private string ProtocolName()
    {
        if (_options.UseSsl)
        {
            return "LDAPS";
        }

        return _options.UseStartTls ? "LDAP+StartTLS" : "LDAP";
    }

    private string BindDnLabel()
    {
        return string.IsNullOrWhiteSpace(_options.BindDn) ? "(leer/anonym)" : _options.BindDn;
    }

    private string BindPasswordLabel()
    {
        return string.IsNullOrEmpty(_options.BindPassword) ? "(leer/nicht gesetzt)" : "gesetzt";
    }

    private string PagingLabel()
    {
        return _options.UsePaging ? "aktiv" : "deaktiviert";
    }

    private bool ValidateBindConfiguration(List<LdapTestStep> steps)
    {
        if (!string.IsNullOrWhiteSpace(_options.BindDn) && string.IsNullOrEmpty(_options.BindPassword))
        {
            steps.Add(new LdapTestStep(
                "Konfiguration",
                false,
                "Bind-Passwort fehlt.",
                "AD_BIND_DN ist gesetzt, aber AD_BIND_PASSWORD ist leer oder kommt nicht im Container an."));
            return false;
        }

        if (string.IsNullOrWhiteSpace(_options.BindDn) && !string.IsNullOrEmpty(_options.BindPassword))
        {
            steps.Add(new LdapTestStep(
                "Konfiguration",
                false,
                "Bind-DN fehlt.",
                "AD_BIND_PASSWORD ist gesetzt, aber AD_BIND_DN fehlt. Das Passwort wuerde sonst ignoriert."));
            return false;
        }

        return true;
    }

    private bool ValidateTransportConfiguration(List<LdapTestStep> steps)
    {
        if (_options.UseSsl && _options.UseStartTls)
        {
            steps.Add(new LdapTestStep(
                "Konfiguration",
                false,
                "TLS-Konfiguration widerspruechlich.",
                "AD_USE_SSL und AD_USE_START_TLS duerfen nicht gleichzeitig aktiv sein. Nutze entweder LDAPS auf Port 636 oder StartTLS auf Port 389."));
            return false;
        }

        return true;
    }

    private static string FriendlyMessage(Exception ex)
    {
        return LdapExceptionFormatter.FriendlyMessage(ex);
    }

    private static string DiagnosticDetail(Exception ex)
    {
        return LdapExceptionFormatter.DiagnosticDetail(ex);
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
}
