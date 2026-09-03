namespace AdGroupUserCompare.Models;

public sealed record AppConfigResponse(
    string Server,
    int Port,
    bool UseSsl,
    bool UseStartTls,
    bool VerifyCertificate,
    string SearchBase,
    string GroupPattern,
    bool BindConfigured,
    string BindDn,
    bool BindPasswordConfigured,
    bool UsePaging,
    bool SettingsSaved);
