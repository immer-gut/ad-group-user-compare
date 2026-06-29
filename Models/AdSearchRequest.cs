using System.ComponentModel.DataAnnotations;

namespace AdGroupUserCompare.Models;

public sealed class AdSearchRequest
{
    [Required]
    public string GroupPattern { get; set; } = "";

    public string? SearchBase { get; set; }

    public string? Server { get; set; }

    public bool OnlyEnabled { get; set; }
}
