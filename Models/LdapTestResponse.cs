namespace AdGroupUserCompare.Models;

public sealed record LdapTestResponse(
    bool Success,
    string Server,
    int Port,
    bool UseSsl,
    string SearchBase,
    bool BindConfigured,
    string BindDn,
    IReadOnlyList<LdapTestStep> Steps);
