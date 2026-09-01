namespace AdGroupUserCompare.Options;

public sealed class LdapOptions
{
    public string Server { get; set; } = "";

    public int Port { get; set; } = 389;

    public bool UseSsl { get; set; }

    public bool UseStartTls { get; set; }

    public string SearchBase { get; set; } = "";

    public string DefaultGroupPattern { get; set; } = "";

    public string BindDn { get; set; } = "";

    public string BindPassword { get; set; } = "";

    public bool UsePaging { get; set; } = true;

    public int PageSize { get; set; } = 500;

    public int MemberRangeSize { get; set; } = 1500;

    public string SettingsPath { get; set; } = "";
}
