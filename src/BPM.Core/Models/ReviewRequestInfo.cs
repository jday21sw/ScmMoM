namespace BPM.Core.Models;

public class ReviewRequestInfo
{
    public string RepoName { get; set; } = string.Empty;
    public int PullRequestNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
