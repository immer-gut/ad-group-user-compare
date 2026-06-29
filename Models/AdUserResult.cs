namespace AdGroupUserCompare.Models;

public sealed record AdUserResult(
    string GroupName,
    string GroupPath,
    string SamAccountName,
    string DisplayName,
    string Mail,
    bool Enabled,
    string Department,
    string Title,
    string DistinguishedName);
