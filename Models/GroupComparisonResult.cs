namespace AdGroupUserCompare.Models;

public sealed record GroupComparisonResult(
    string Status,
    string GroupName,
    string UserA,
    string UserB,
    string GroupPathA,
    string GroupPathB);
