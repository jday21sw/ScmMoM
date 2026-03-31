namespace ScmMoM.Core.Models;

public class NotificationInfo
{
    public string AccountId { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string RepoName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public bool IsUnread { get; set; }
}
