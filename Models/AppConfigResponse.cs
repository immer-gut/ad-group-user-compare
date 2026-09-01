namespace AdGroupUserCompare.Models;

public sealed record AppConfigResponse(
    string Server,
    string SearchBase,
    bool UseSsl,
    bool UseStartTls,
    bool BindConfigured);
