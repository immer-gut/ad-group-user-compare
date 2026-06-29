using AdGroupUserCompare.Models;

namespace AdGroupUserCompare.Services;

public interface IAdGroupLookupService
{
    Task<AdSearchResponse> SearchAsync(AdSearchRequest request, CancellationToken cancellationToken);
}
