namespace AdGroupUserCompare.Models;

public sealed class LdapTestRequest
{
    public string? GroupPattern { get; set; }

    public string? SearchBase { get; set; }

    public string? Server { get; set; }
}
