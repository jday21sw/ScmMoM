namespace ScmMoM.Core.Models;

public class ReviewRequestInfo
{
    public string AccountId { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string RepoName { get; set; } = string.Empty;
    public int PullRequestNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
