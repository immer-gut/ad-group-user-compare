using Novell.Directory.Ldap;

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
        var constraints = new LdapSearchConstraints
        {
            ReferralFollowing = false,
            TimeLimit = 20_000
        };

        if (usePaging)
        {
            var options = new SearchOptions(searchBase, scope, filter, attributes, false, constraints);
            return await _connection.SearchUsingSimplePagingAsync(
                options,
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
        while (await search.HasMoreAsync(cancellationToken))
        {
            entries.Add(await search.NextAsync(cancellationToken));
        }

        return entries;
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
