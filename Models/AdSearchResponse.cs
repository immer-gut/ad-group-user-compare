namespace AdGroupUserCompare.Models;

public sealed record AdSearchResponse(
    IReadOnlyList<AdUserResult> Results,
    int GroupCount,
    int UserCount);
