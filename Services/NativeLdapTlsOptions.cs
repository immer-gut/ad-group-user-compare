using AdGroupUserCompare.Options;

namespace AdGroupUserCompare.Services;

internal static class NativeLdapTlsOptions
{
    private const string CaCertificatesPath = "/etc/ssl/certs/ca-certificates.crt";
    private static readonly object Sync = new();

    public static void Apply(LdapOptions options)
    {
        if (!options.UseSsl && !options.UseStartTls)
        {
            return;
        }

        lock (Sync)
        {
            Environment.SetEnvironmentVariable("LDAPTLS_CACERT", CaCertificatesPath);
            Environment.SetEnvironmentVariable("LDAPTLS_REQCERT", options.VerifyCertificate ? "demand" : "never");
        }
    }
}
