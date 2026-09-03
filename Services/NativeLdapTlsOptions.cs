namespace AdGroupUserCompare.Services;

internal static class NativeLdapTlsOptions
{
    private const string CaCertificatesPath = "/etc/ssl/certs/ca-certificates.crt";
    private static readonly object Sync = new();

    public static void Apply(bool useSsl, bool useStartTls, bool verifyCertificate)
    {
        if (!useSsl && !useStartTls)
        {
            return;
        }

        lock (Sync)
        {
            Environment.SetEnvironmentVariable("LDAPTLS_CACERT", CaCertificatesPath);
            Environment.SetEnvironmentVariable("LDAPTLS_REQCERT", verifyCertificate ? "demand" : "never");
        }
    }
}
