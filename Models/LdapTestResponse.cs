namespace AdGroupUserCompare.Models;

public sealed record LdapTestResponse(
    bool Success,
    string Server,
    int Port,
    bool UseSsl,
    bool UseStartTls,
    string SearchBase,
    bool BindConfigured,
    string BindDn,
    bool BindPasswordConfigured,
    bool UsePaging,
    IReadOnlyList<LdapTestStep> Steps);
