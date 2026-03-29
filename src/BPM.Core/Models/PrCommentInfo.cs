namespace BPM.Core.Models;

public class PrCommentInfo
{
    public string Author { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string HtmlUrl { get; set; } = string.Empty;
}
