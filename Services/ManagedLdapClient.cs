using Novell.Directory.Ldap;
using Novell.Directory.Ldap.Controls;

namespace AdGroupUserCompare.Services;

internal sealed class ManagedLdapClient : IDisposable
{
    private readonly LdapConnection _connection;

    public ManagedLdapClient(LdapEndpoint endpoint, bool verifyCertificate, TimeSpan timeout)
    {
        var options = new LdapConnectionOptions();
        if (endpoint.UseSsl)
        {
            options.UseSsl();
        }

        if (!verifyCertificate)
        {
            options.ConfigureRemoteCertificateValidationCallback((_, _, _, _) => true);
        }

        _connection = new LdapConnection(options)
        {
            ConnectionTimeout = checked((int)timeout.TotalMilliseconds)
        };
    }

    public bool Connected => _connection.Connected;

    public bool Tls => _connection.Tls || _connection.SecureSocketLayer;

    public bool Bound => _connection.Bound;

    public async Task ConnectAsync(LdapEndpoint endpoint, CancellationToken cancellationToken)
    {
        await _connection.ConnectAsync(endpoint.Server, endpoint.Port, cancellationToken);
        if (!_connection.Connected)
        {
            throw new InvalidOperationException("LDAP-Client meldet nach dem Verbindungsaufbau keine aktive Verbindung.");
        }
    }

    public async Task StartTlsAsync(CancellationToken cancellationToken)
    {
        await _connection.StartTlsAsync(cancellationToken);
        if (!_connection.Tls)
        {
            throw new InvalidOperationException("StartTLS wurde ausgefuehrt, aber der LDAP-Client meldet keine aktive TLS-Verbindung.");
        }
    }

    public async Task BindAsync(string? bindDn, string? bindPassword, CancellationToken cancellationToken)
    {
        await _connection.BindAsync(bindDn ?? "", bindPassword ?? "", cancellationToken);
        if (!_connection.Bound)
        {
            throw new InvalidOperationException("LDAP-Client meldet nach dem Bind keine gebundene Verbindung.");
        }
    }

    public async Task<List<LdapEntry>> SearchAsync(
        string searchBase,
        int scope,
        string filter,
        string[] attributes,
        bool usePaging,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var result = await SearchWithMetadataAsync(
            searchBase,
            scope,
            filter,
            attributes,
            usePaging,
            pageSize,
            cancellationToken);
        return result.Entries;
    }

    public async Task<ManagedLdapSearchResult> SearchWithMetadataAsync(
        string searchBase,
        int scope,
        string filter,
        string[] attributes,
        bool usePaging,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var constraints = new LdapSearchConstraints
        {
            ReferralFollowing = false,
            TimeLimit = 20_000
        };

        if (usePaging)
        {
            return await SearchUsingPagingAsync(
                searchBase,
                scope,
                filter,
                attributes,
                constraints,
                Math.Max(1, pageSize),
                cancellationToken);
        }

        var search = await _connection.SearchAsync(
            searchBase,
            scope,
            filter,
            attributes,
            false,
            constraints,
            cancellationToken);
        var entries = new List<LdapEntry>();
        var skippedReferralCount = await ReadEntriesAsync(search, entries, cancellationToken);
        return new ManagedLdapSearchResult(entries, skippedReferralCount);
    }

    private async Task<ManagedLdapSearchResult> SearchUsingPagingAsync(
        string searchBase,
        int scope,
        string filter,
        string[] attributes,
        LdapSearchConstraints constraints,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var entries = new List<LdapEntry>();
        var skippedReferralCount = 0;
        var cookie = SimplePagedResultsControl.GetEmptyCookie;

        while (true)
        {
            constraints.BatchSize = 0;
            constraints.SetControls(new SimplePagedResultsControl(pageSize, cookie));
            var search = await _connection.SearchAsync(
                searchBase,
                scope,
                filter,
                attributes,
                false,
                constraints,
                cancellationToken);

            skippedReferralCount += await ReadEntriesAsync(search, entries, cancellationToken);
            var pageControl = search.ResponseControls?
                .OfType<SimplePagedResultsControl>()
                .SingleOrDefault();
            if (pageControl is null)
            {
                throw new LdapException("LDAP-Server hat keine Paging-Antwort geliefert.");
            }

            if (pageControl.IsEmptyCookie())
            {
                break;
            }

            cookie = pageControl.Cookie;
        }

        return new ManagedLdapSearchResult(entries, skippedReferralCount);
    }

    private static async Task<int> ReadEntriesAsync(
        ILdapSearchResults search,
        List<LdapEntry> entries,
        CancellationToken cancellationToken)
    {
        var skippedReferralCount = 0;
        while (await search.HasMoreAsync(cancellationToken))
        {
            try
            {
                entries.Add(await search.NextAsync(cancellationToken));
            }
            catch (LdapReferralException)
            {
                skippedReferralCount++;
            }
        }

        return skippedReferralCount;
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}

internal sealed record ManagedLdapSearchResult(List<LdapEntry> Entries, int SkippedReferralCount);
