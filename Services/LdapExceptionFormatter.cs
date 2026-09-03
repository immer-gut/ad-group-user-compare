using System.DirectoryServices.Protocols;
using System.Security.Authentication;

namespace AdGroupUserCompare.Services;

internal static class LdapExceptionFormatter
{
    public static string FriendlyMessage(Exception ex)
    {
        if (RequiresStrongAuthentication(ex))
        {
            return "Starke LDAP-Authentifizierung erforderlich. Setze AD_USE_SSL=true mit AD_LDAP_PORT=636 oder AD_USE_START_TLS=true mit AD_LDAP_PORT=389.";
        }

        return ex switch
        {
            LdapException ldapException => ldapException.ErrorCode switch
            {
                49 => "Bind fehlgeschlagen: Benutzername oder Passwort wird abgelehnt.",
                81 => "Server nicht erreichbar, Port/SSL passt nicht oder das Zertifikat wird abgelehnt.",
                91 => "LDAP-Verbindung konnte nicht hergestellt werden.",
                _ => $"LDAP-Fehler {ldapException.ErrorCode}: {ldapException.Message}"
            },
            AuthenticationException => "TLS-Handshake fehlgeschlagen. Pruefe, ob auf diesem Port wirklich LDAPS laeuft und ob Zertifikat/TLS-Version passen.",
            TlsOperationException => "StartTLS fehlgeschlagen. Pruefe Port 389, Zertifikat und ob der Domain Controller StartTLS anbietet.",
            DirectoryOperationException directoryOperationException => $"LDAP-Operation fehlgeschlagen: {directoryOperationException.Message}",
            _ => ex.Message
        };
    }

    public static string DiagnosticDetail(Exception ex)
    {
        var parts = new List<string> { ex.GetType().Name };
        if (!string.IsNullOrWhiteSpace(ex.Message))
        {
            parts.Add(ex.Message);
        }

        if (ex is LdapException ldapException)
        {
            parts.Add($"ErrorCode={ldapException.ErrorCode}");
            if (!string.IsNullOrWhiteSpace(ldapException.ServerErrorMessage))
            {
                parts.Add(ldapException.ServerErrorMessage);
            }
        }

        if (RequiresStrongAuthentication(ex))
        {
            parts.Add("AD verlangt signierten oder verschluesselten LDAP-Bind. Empfohlen: LDAPS auf Port 636.");
        }

        if (ex.InnerException is not null)
        {
            parts.Add($"Inner={ex.InnerException.Message}");
        }

        return string.Join(" | ", parts);
    }

    private static bool RequiresStrongAuthentication(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("strong authentication is required", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (current is LdapException ldapException &&
                ldapException.ServerErrorMessage?.Contains("strong authentication is required", StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }
        }

        return false;
    }
}
