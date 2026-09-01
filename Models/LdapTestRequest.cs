namespace AdGroupUserCompare.Models;

public sealed class LdapTestRequest
{
    public string? GroupPattern { get; set; }

    public string? SearchBase { get; set; }

    public string? Server { get; set; }

    public int? Port { get; set; }

    public bool? UseSsl { get; set; }

    public bool? UseStartTls { get; set; }

    public string? BindDn { get; set; }

    public string? BindPassword { get; set; }

    public bool ClearBindPassword { get; set; }

    public bool? UsePaging { get; set; }
}
