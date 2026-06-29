using AdGroupUserCompare.Models;

namespace AdGroupUserCompare.Services;

public interface ILdapDiagnosticService
{
    Task<LdapTestResponse> TestAsync(LdapTestRequest request, CancellationToken cancellationToken);
}
