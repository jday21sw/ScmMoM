using BPM.Core.Models;

namespace BPM.Core.Services;

public class NotificationService
{
    private HashSet<string> _previousReviewRequestKeys = new();
    private HashSet<string> _previousFailedActionKeys = new();

    public record NotificationEvent(string Title, string Message, string Url);

    public List<NotificationEvent> DetectNewReviewRequests(IReadOnlyList<ReviewRequestInfo> current)
    {
        var currentKeys = current.Select(GetReviewKey).ToHashSet();
        var newItems = current
            .Where(r => !_previousReviewRequestKeys.Contains(GetReviewKey(r)))
            .Select(r => new NotificationEvent(
                "New Review Request",
                $"{r.RepoName} #{r.PullRequestNumber}: {r.Title} (by {r.Author})",
                r.Url))
            .ToList();

        _previousReviewRequestKeys = currentKeys;
        return newItems;
    }

    public List<NotificationEvent> DetectFailedActions(IReadOnlyList<ActionRunInfo> current)
    {
        var currentFailed = current
            .Where(a => a.Conclusion is "failure")
            .ToList();

        var currentKeys = currentFailed.Select(GetActionKey).ToHashSet();
        var newFailures = currentFailed
            .Where(a => !_previousFailedActionKeys.Contains(GetActionKey(a)))
            .Select(a => new NotificationEvent(
                "Action Failed",
                $"{a.RepoName}: {a.WorkflowName} #{a.RunNumber} on {a.Branch}",
                a.Url))
            .ToList();

        _previousFailedActionKeys = currentKeys;
        return newFailures;
    }

    private static string GetReviewKey(ReviewRequestInfo r) => $"{r.RepoName}/{r.PullRequestNumber}";
    private static string GetActionKey(ActionRunInfo a) => $"{a.RepoName}/{a.WorkflowName}/{a.RunNumber}";
}
