namespace ScmMoM.Core.Models;

public class IssueInfo
{
    public string RepoName { get; set; } = string.Empty;
    public int Number { get; set; }
    public string Title { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<string> Labels { get; set; } = new();
    public string? Assignee { get; set; }
}
