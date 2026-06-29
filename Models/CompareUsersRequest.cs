namespace AdGroupUserCompare.Models;

public sealed class CompareUsersRequest
{
    public string UserA { get; set; } = "";

    public string UserB { get; set; } = "";

    public List<AdUserResult> Results { get; set; } = [];
}
