using AdGroupUserCompare.Options;

namespace AdGroupUserCompare.Services;

internal sealed record LdapEndpoint(string Server, int Port, bool UseSsl, bool UseStartTls);

internal static class LdapEndpointResolver
{
    public static LdapEndpoint Resolve(LdapOptions options)
    {
        return Resolve(options.Server, options.Port, options.UseSsl, options.UseStartTls);
    }

    public static LdapEndpoint Resolve(string? serverValue, int port, bool useSsl, bool useStartTls)
    {
        var server = serverValue?.Trim() ?? "";
        if (server.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(server, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
            {
                throw new InvalidOperationException("LDAP-Server-URL ist ungueltig.");
            }

            var scheme = uri.Scheme.ToLowerInvariant();
            if (scheme is not ("ldap" or "ldaps"))
            {
                throw new InvalidOperationException("LDAP-Server-URL muss mit ldap:// oder ldaps:// beginnen.");
            }

            if (!string.IsNullOrEmpty(uri.UserInfo) ||
                uri.AbsolutePath != "/" ||
                !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment))
            {
                throw new InvalidOperationException("LDAP-Server-URL darf nur Host und optional Port enthalten.");
            }

            useSsl = scheme == "ldaps";
            useStartTls = scheme == "ldap" && useStartTls;
            port = uri.IsDefaultPort
                ? (useSsl ? 636 : 389)
                : uri.Port;
            server = uri.DnsSafeHost;
        }

        if (port is < 1 or > 65535)
        {
            throw new InvalidOperationException("LDAP-Port muss zwischen 1 und 65535 liegen.");
        }

        if (useSsl && useStartTls)
        {
            throw new InvalidOperationException("AD_USE_SSL und AD_USE_START_TLS duerfen nicht gleichzeitig aktiv sein. Nutze entweder LDAPS auf Port 636 oder StartTLS auf Port 389.");
        }

        if (useSsl && port == 389)
        {
            throw new InvalidOperationException("LDAPS ist aktiv, aber Port 389 ist gesetzt. Nutze fuer LDAPS Port 636 oder fuer Port 389 StartTLS.");
        }

        if (useStartTls && port == 636)
        {
            throw new InvalidOperationException("StartTLS ist aktiv, aber Port 636 ist gesetzt. Nutze fuer StartTLS Port 389 oder fuer Port 636 LDAPS.");
        }

        return new LdapEndpoint(server, port, useSsl, useStartTls);
    }

    public static void Apply(LdapOptions options, LdapEndpoint endpoint)
    {
        options.Server = endpoint.Server;
        options.Port = endpoint.Port;
        options.UseSsl = endpoint.UseSsl;
        options.UseStartTls = endpoint.UseStartTls;
    }
}
