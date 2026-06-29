namespace AdGroupUserCompare.Models;

public sealed record AdSearchResponse(
    IReadOnlyList<AdUserResult> Results,
    IReadOnlyList<string> GroupNames,
    int GroupCount,
    int UserCount);
