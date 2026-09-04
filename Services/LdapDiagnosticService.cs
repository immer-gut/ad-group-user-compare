using System.Net.Security;
using System.Net.Sockets;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using AdGroupUserCompare.Models;
using AdGroupUserCompare.Options;
using Novell.Directory.Ldap;

namespace AdGroupUserCompare.Services;

public sealed class LdapDiagnosticService(LdapSettingsStore settings) : ILdapDiagnosticService
{
    private readonly LdapOptions _options = settings.Current;

    public async Task<LdapTestResponse> TestAsync(LdapTestRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var options = BuildTestOptions(request);
        var steps = new List<LdapTestStep>();
        try
        {
            LdapEndpointResolver.Apply(options, LdapEndpointResolver.Resolve(options));
        }
        catch (InvalidOperationException ex)
        {
            steps.Add(new LdapTestStep("Konfiguration", false, ex.Message, "Pruefe LDAP-Server-URL, Port und TLS-Auswahl."));
            return BuildResponse(false, options, options.Server, options.SearchBase, steps);
        }

        var server = FirstNonEmpty(options.Server);
        var searchBase = FirstNonEmpty(options.SearchBase);
        var groupPattern = FirstNonEmpty(request.GroupPattern, options.DefaultGroupPattern);

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

        steps.Add(new LdapTestStep(
            "Konfiguration",
            true,
            "Pflichtwerte vorhanden.",
            $"{ProtocolName(options)} {server}:{options.Port}, SearchBase {searchBase}, Bind-DN {BindDnLabel(options)}, Passwort {BindPasswordLabel(options)}, Zertifikat {CertificateLabel(options)}, Paging {PagingLabel(options)}, Managed-LDAP-Client, Referrals aus"));

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

        if (options.UseSsl &&
            !await TryStepAsync(
                "TLS-Handshake",
                steps,
                token => ProbeLdapsHandshakeAsync(server, options.Port, options.VerifyCertificate, token),
                "LDAPS-TLS-Handshake erfolgreich.",
                cancellationToken))
        {
            return BuildResponse(false, options, server, searchBase, steps);
        }

        var endpoint = new LdapEndpoint(server, options.Port, options.UseSsl, options.UseStartTls);
        using var activeConnection = CreateConnection(endpoint, options);
        if (!await TryStepAsync(
            "Verbindung",
            steps,
            async token =>
            {
                await activeConnection.ConnectAsync(endpoint, token);
                return $"LDAP-Verbindung zu {server}:{options.Port} wurde geoeffnet.";
            },
            "LDAP-Verbindung geoeffnet.",
            cancellationToken))
        {
            return BuildResponse(false, options, server, searchBase, steps);
        }

        if (options.UseStartTls &&
            !await TryStepAsync(
                "StartTLS",
                steps,
                async token =>
                {
                    await activeConnection.StartTlsAsync(token);
                    return "TLS ist auf der geoeffneten LDAP-Verbindung aktiv.";
                },
                "StartTLS erfolgreich.",
                cancellationToken))
        {
            return BuildResponse(false, options, server, searchBase, steps);
        }

        if (!await TryStepAsync(
            "Bind",
            steps,
            token => BindConnectionAsync(activeConnection, options, token),
            "LDAP-Bind erfolgreich.",
            cancellationToken))
        {
            return BuildResponse(false, options, server, searchBase, steps);
        }

        if (!await TryStepAsync(
            "SearchBase",
            steps,
            token => ProbeSearchBaseAsync(activeConnection, searchBase, token),
            "SearchBase ist lesbar.",
            cancellationToken))
        {
            return BuildResponse(false, options, server, searchBase, steps);
        }

        if (!string.IsNullOrWhiteSpace(groupPattern))
        {
            if (!await TryStepAsync(
                "Gruppenmuster ohne Paging",
                steps,
                token => ProbeGroupPatternAsync(activeConnection, searchBase, groupPattern, usePaging: false, token),
                "Gruppensuche ohne Paging erfolgreich.",
                cancellationToken))
            {
                return BuildResponse(false, options, server, searchBase, steps);
            }

            if (options.UsePaging)
            {
                await TryStepAsync(
                    "Gruppenmuster mit Paging",
                    steps,
                    token => ProbeGroupPatternAsync(activeConnection, searchBase, groupPattern, usePaging: true, token),
                    "Gruppensuche mit Paging erfolgreich.",
                    cancellationToken);
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

        return BuildResponse(steps.All(step => step.Success), options, server, searchBase, steps);
    }

    private static ManagedLdapClient CreateConnection(LdapEndpoint endpoint, LdapOptions options)
    {
        return new ManagedLdapClient(endpoint, options.VerifyCertificate, TimeSpan.FromSeconds(20));
    }

    private static async Task<string> BindConnectionAsync(
        ManagedLdapClient connection,
        LdapOptions options,
        CancellationToken cancellationToken)
    {
        await connection.BindAsync(options.BindDn, options.BindPassword, cancellationToken);
        return $"Expliziter LDAP-Bind mit {BindDnLabel(options)} wurde ausgefuehrt.";
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

    private static async Task<string> ProbeLdapsHandshakeAsync(
        string server,
        int port,
        bool verifyCertificate,
        CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));

        try
        {
            await client.ConnectAsync(server, port, timeout.Token);
            var certificateErrors = SslPolicyErrors.None;
            X509Certificate2? serverCertificate = null;
            using var sslStream = new SslStream(
                client.GetStream(),
                leaveInnerStreamOpen: false,
                (_, certificate, _, errors) =>
                {
                    certificateErrors = errors;
                    if (certificate is not null)
                    {
                        serverCertificate = certificate as X509Certificate2 ?? new X509Certificate2(certificate);
                    }

                    return true;
                });

            await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = server,
                EnabledSslProtocols = SslProtocols.None,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            }, timeout.Token);

            var detail = BuildTlsDetail(sslStream, serverCertificate, certificateErrors, verifyCertificate);
            if (verifyCertificate && certificateErrors != SslPolicyErrors.None)
            {
                throw new InvalidOperationException($"Zertifikatspruefung fehlgeschlagen: {detail}");
            }

            return detail;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"TLS-Handshake zu {server}:{port} nach 8 Sekunden abgebrochen.");
        }
    }

    private static string BuildTlsDetail(
        SslStream sslStream,
        X509Certificate2? certificate,
        SslPolicyErrors certificateErrors,
        bool verifyCertificate)
    {
        var parts = new List<string>
        {
            $"Protokoll {sslStream.SslProtocol}"
        };

        if (certificate is not null)
        {
            parts.Add($"Zertifikat {certificate.Subject}");
            parts.Add($"Issuer {certificate.Issuer}");
            parts.Add($"Gueltig bis {certificate.NotAfter:yyyy-MM-dd HH:mm}");
        }

        if (certificateErrors == SslPolicyErrors.None)
        {
            parts.Add("Zertifikatsstatus ok");
        }
        else if (verifyCertificate)
        {
            parts.Add($"Zertifikatsfehler {certificateErrors}");
        }
        else
        {
            parts.Add($"Zertifikatsfehler ignoriert: {certificateErrors}");
        }

        return string.Join(" | ", parts);
    }

    private static async Task<string> ProbeSearchBaseAsync(
        ManagedLdapClient connection,
        string searchBase,
        CancellationToken cancellationToken)
    {
        await connection.SearchAsync(
            searchBase,
            LdapConnection.ScopeBase,
            "(objectClass=*)",
            ["distinguishedName", "name"],
            usePaging: false,
            pageSize: 1,
            cancellationToken);
        return "Basisabfrage wurde auf der gebundenen Verbindung ausgefuehrt.";
    }

    private static async Task<string> ProbeGroupPatternAsync(
        ManagedLdapClient connection,
        string searchBase,
        string groupPattern,
        bool usePaging,
        CancellationToken cancellationToken)
    {
        var result = await connection.SearchWithMetadataAsync(
            searchBase,
            LdapConnection.ScopeSub,
            $"(&(objectClass=group)(cn={EscapeLdapFilterValue(groupPattern)}))",
            ["distinguishedName", "cn", "name"],
            usePaging,
            pageSize: 10,
            cancellationToken);
        var referralDetail = result.SkippedReferralCount == 0
            ? "Keine LDAP-Referrals erhalten."
            : $"{result.SkippedReferralCount} LDAP-Referral(s) wie konfiguriert uebersprungen.";
        return $"{result.Entries.Count} Gruppe(n) in der Testabfrage gefunden. {referralDetail}";
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

    private static string BindDnLabel(LdapOptions options)
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
