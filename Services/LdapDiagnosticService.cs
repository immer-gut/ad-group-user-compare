using System.DirectoryServices.Protocols;
using System.Net;
using System.Net.Sockets;
using AdGroupUserCompare.Models;
using AdGroupUserCompare.Options;

namespace AdGroupUserCompare.Services;

public sealed class LdapDiagnosticService(LdapSettingsStore settings) : ILdapDiagnosticService
{
    private readonly LdapOptions _options = settings.Current;

    public async Task<LdapTestResponse> TestAsync(LdapTestRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var options = BuildTestOptions(request);
        var server = FirstNonEmpty(options.Server);
        var searchBase = FirstNonEmpty(options.SearchBase);
        var groupPattern = FirstNonEmpty(request.GroupPattern, options.DefaultGroupPattern);
        var steps = new List<LdapTestStep>();

        if (string.IsNullOrWhiteSpace(server))
        {
            steps.Add(new LdapTestStep("Konfiguration", false, "LDAP-Server fehlt.", "Setze AD_LDAP_SERVER oder Ad__Server im Portainer Stack."));
            return BuildResponse(false, options, server, searchBase, steps);
        }

        if (string.IsNullOrWhiteSpace(searchBase))
        {
            steps.Add(new LdapTestStep("Konfiguration", false, "SearchBase fehlt.", "Setze AD_SEARCH_BASE oder Ad__SearchBase, z. B. DC=example,DC=local."));
            return BuildResponse(false, options, server, searchBase, steps);
        }

        if (!ValidateBindConfiguration(options, steps))
        {
            return BuildResponse(false, options, server, searchBase, steps);
        }

        if (!ValidateTransportConfiguration(options, steps))
        {
            return BuildResponse(false, options, server, searchBase, steps);
        }

        steps.Add(new LdapTestStep(
            "Konfiguration",
            true,
            "Pflichtwerte vorhanden.",
            $"{ProtocolName(options)} {server}:{options.Port}, SearchBase {searchBase}, Bind-DN {BindDnLabel(options)}, Passwort {BindPasswordLabel(options)}, Zertifikat {CertificateLabel(options)}, Paging {PagingLabel(options)}, Referrals aus"));

        if (!TryStep("DNS", steps, () => ResolveServerAddresses(server), "LDAP-Servername aufgeloest."))
        {
            return BuildResponse(false, options, server, searchBase, steps);
        }

        if (!await TryStepAsync(
            "TCP-Port",
            steps,
            token => ProbeTcpPortAsync(server, options.Port, token),
            "TCP-Port erreichbar.",
            cancellationToken))
        {
            return BuildResponse(false, options, server, searchBase, steps);
        }

        LdapConnection? connection = null;
        if (!TryStep(
            "Verbindung",
            steps,
            () =>
            {
                connection = CreateConnection(server, options);
                return "LDAP-Client wurde initialisiert.";
            },
            "Verbindungsaufbau vorbereitet."))
        {
            return BuildResponse(false, options, server, searchBase, steps);
        }

        var activeConnection = connection ?? throw new InvalidOperationException("LDAP-Verbindung wurde nicht initialisiert.");
        using (activeConnection)
        {
            if (options.UseStartTls &&
                !TryStep("StartTLS", steps, () => StartTransportLayerSecurity(activeConnection), "StartTLS erfolgreich."))
            {
                return BuildResponse(false, options, server, searchBase, steps);
            }

            if (!TryStep("Bind", steps, () => BindConnection(activeConnection, options), "LDAP-Bind erfolgreich."))
            {
                return BuildResponse(false, options, server, searchBase, steps);
            }

            if (!TryStep(
                "SearchBase",
                steps,
                () => ProbeSearchBase(activeConnection, searchBase),
                "SearchBase ist lesbar."))
            {
                return BuildResponse(false, options, server, searchBase, steps);
            }

            if (!string.IsNullOrWhiteSpace(groupPattern))
            {
                if (!TryStep(
                    "Gruppenmuster ohne Paging",
                    steps,
                    () => ProbeGroupPattern(activeConnection, searchBase, groupPattern, usePaging: false),
                    "Gruppensuche ohne Paging erfolgreich."))
                {
                    return BuildResponse(false, options, server, searchBase, steps);
                }

                if (options.UsePaging)
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
                        "Paging ist deaktiviert. Die App nutzt Gruppensuche ohne PageResult-Control."));
                }
            }
            else
            {
                steps.Add(new LdapTestStep("Gruppenmuster", true, "Uebersprungen.", "Trage ein Gruppenmuster ein, um auch die Gruppensuche zu testen."));
            }
        }

        return BuildResponse(steps.All(step => step.Success), options, server, searchBase, steps);
    }

    private LdapConnection CreateConnection(string server, LdapOptions options)
    {
        var identifier = new LdapDirectoryIdentifier(server, options.Port, fullyQualifiedDnsHostName: false, connectionless: false);
        var connection = new LdapConnection(identifier)
        {
            AuthType = string.IsNullOrWhiteSpace(options.BindDn) ? AuthType.Anonymous : AuthType.Basic,
            Credential = CreateCredential(options),
            Timeout = TimeSpan.FromSeconds(20)
        };

        connection.SessionOptions.ProtocolVersion = 3;
        ConfigureCertificateValidation(connection, options);
        connection.SessionOptions.SecureSocketLayer = options.UseSsl;
        connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
        return connection;
    }

    private static void ConfigureCertificateValidation(LdapConnection connection, LdapOptions options)
    {
        if (!options.VerifyCertificate)
        {
            connection.SessionOptions.VerifyServerCertificate = (_, _) => true;
        }
    }

    private void StartTransportLayerSecurity(LdapConnection connection)
    {
        connection.SessionOptions.StartTransportLayerSecurity(new DirectoryControlCollection());
    }

    private string BindConnection(LdapConnection connection, LdapOptions options)
    {
        var credential = CreateCredential(options);
        if (credential is null)
        {
            connection.Bind();
            return "Anonymer LDAP-Bind wurde ausgefuehrt.";
        }

        connection.Bind(credential);
        return $"Expliziter LDAP-Bind mit {BindDnLabel(options)} wurde ausgefuehrt.";
    }

    private NetworkCredential? CreateCredential(LdapOptions options)
    {
        return string.IsNullOrWhiteSpace(options.BindDn)
            ? null
            : new NetworkCredential(options.BindDn, options.BindPassword);
    }

    private static string ResolveServerAddresses(string server)
    {
        var addresses = Dns.GetHostAddresses(server);
        if (addresses.Length == 0)
        {
            throw new InvalidOperationException("DNS hat keine Adresse fuer den LDAP-Server geliefert.");
        }

        var shownAddresses = addresses
            .Take(6)
            .Select(address => address.ToString())
            .ToList();
        var suffix = addresses.Length > shownAddresses.Count ? " ..." : "";
        return string.Join(", ", shownAddresses) + suffix;
    }

    private static async Task<string> ProbeTcpPortAsync(string server, int port, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            await client.ConnectAsync(server, port, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"TCP-Verbindung zu {server}:{port} nach 5 Sekunden abgebrochen.");
        }

        return $"TCP {server}:{port} ist erreichbar.";
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

    private static async Task<bool> TryStepAsync(
        string name,
        List<LdapTestStep> steps,
        Func<CancellationToken, Task<string>> action,
        string successMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            var detail = await action(cancellationToken);
            steps.Add(new LdapTestStep(name, true, successMessage, detail));
            return true;
        }
        catch (Exception ex)
        {
            steps.Add(new LdapTestStep(name, false, FriendlyMessage(ex), DiagnosticDetail(ex)));
            return false;
        }
    }

    private static LdapTestResponse BuildResponse(bool success, LdapOptions options, string server, string searchBase, IReadOnlyList<LdapTestStep> steps)
    {
        return new LdapTestResponse(
            success,
            server,
            options.Port,
            options.UseSsl,
            options.UseStartTls,
            options.VerifyCertificate,
            searchBase,
            !string.IsNullOrWhiteSpace(options.BindDn),
            options.BindDn,
            !string.IsNullOrEmpty(options.BindPassword),
            options.UsePaging,
            steps);
    }

    private string ProtocolName(LdapOptions options)
    {
        if (options.UseSsl)
        {
            return "LDAPS";
        }

        return options.UseStartTls ? "LDAP+StartTLS" : "LDAP";
    }

    private string BindDnLabel(LdapOptions options)
    {
        return string.IsNullOrWhiteSpace(options.BindDn) ? "(leer/anonym)" : options.BindDn;
    }

    private string BindPasswordLabel(LdapOptions options)
    {
        return string.IsNullOrEmpty(options.BindPassword) ? "(leer/nicht gesetzt)" : "gesetzt";
    }

    private string PagingLabel(LdapOptions options)
    {
        return options.UsePaging ? "aktiv" : "deaktiviert";
    }

    private string CertificateLabel(LdapOptions options)
    {
        return options.VerifyCertificate ? "wird geprueft" : "Pruefung deaktiviert";
    }

    private static bool ValidateBindConfiguration(LdapOptions options, List<LdapTestStep> steps)
    {
        if (!string.IsNullOrWhiteSpace(options.BindDn) && string.IsNullOrEmpty(options.BindPassword))
        {
            steps.Add(new LdapTestStep(
                "Konfiguration",
                false,
                "Bind-Passwort fehlt.",
                "AD_BIND_DN ist gesetzt, aber AD_BIND_PASSWORD ist leer oder kommt nicht im Container an."));
            return false;
        }

        if (string.IsNullOrWhiteSpace(options.BindDn) && !string.IsNullOrEmpty(options.BindPassword))
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

    private LdapOptions BuildTestOptions(LdapTestRequest request)
    {
        var options = LdapSettingsStore.Clone(_options);
        options.Server = request.Server?.Trim() ?? options.Server;
        options.Port = request.Port is >= 1 and <= 65535 ? request.Port.Value : options.Port;
        options.UseSsl = request.UseSsl ?? options.UseSsl;
        options.UseStartTls = request.UseStartTls ?? options.UseStartTls;
        options.VerifyCertificate = request.VerifyCertificate ?? options.VerifyCertificate;
        options.SearchBase = request.SearchBase?.Trim() ?? options.SearchBase;
        options.DefaultGroupPattern = request.GroupPattern?.Trim() ?? options.DefaultGroupPattern;
        options.BindDn = request.BindDn?.Trim() ?? options.BindDn;
        if (request.ClearBindPassword)
        {
            options.BindPassword = "";
        }
        else if (!string.IsNullOrEmpty(request.BindPassword))
        {
            options.BindPassword = request.BindPassword;
        }
        options.UsePaging = request.UsePaging ?? options.UsePaging;
        return options;
    }

    private static bool ValidateTransportConfiguration(LdapOptions options, List<LdapTestStep> steps)
    {
        if (options.UseSsl && options.UseStartTls)
        {
            steps.Add(new LdapTestStep(
                "Konfiguration",
                false,
                "TLS-Konfiguration widerspruechlich.",
                "AD_USE_SSL und AD_USE_START_TLS duerfen nicht gleichzeitig aktiv sein. Nutze entweder LDAPS auf Port 636 oder StartTLS auf Port 389."));
            return false;
        }

        if (options.UseSsl && options.Port == 389)
        {
            steps.Add(new LdapTestStep(
                "Konfiguration",
                false,
                "LDAPS-Port passt nicht.",
                "LDAPS / SSL ist aktiv, aber Port 389 ist gesetzt. Nutze fuer LDAPS Port 636 oder fuer Port 389 StartTLS."));
            return false;
        }

        if (options.UseStartTls && options.Port == 636)
        {
            steps.Add(new LdapTestStep(
                "Konfiguration",
                false,
                "StartTLS-Port passt nicht.",
                "StartTLS ist aktiv, aber Port 636 ist gesetzt. Nutze fuer StartTLS Port 389 oder fuer Port 636 LDAPS."));
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
