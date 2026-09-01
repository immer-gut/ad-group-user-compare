namespace AdGroupUserCompare.Models;

public sealed class LdapSettingsRequest
{
    public string? Server { get; set; }

    public int Port { get; set; } = 636;

    public bool UseSsl { get; set; } = true;

    public bool UseStartTls { get; set; }

    public string? SearchBase { get; set; }

    public string? GroupPattern { get; set; }

    public string? BindDn { get; set; }

    public string? BindPassword { get; set; }

    public bool ClearBindPassword { get; set; }

    public bool UsePaging { get; set; } = true;
}
