namespace AdGroupUserCompare.Models;

public sealed record LdapTestStep(
    string Name,
    bool Success,
    string Message,
    string? Detail = null);
