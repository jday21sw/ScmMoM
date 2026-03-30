using ScmMoM.Core.Models;

namespace ScmMoM.Core.Services;

public interface IScmProvider
{
    string AccountId { get; }
    ScmProviderType ProviderType { get; }

    void Initialize(string token, string username);
    Task<string> ValidateTokenAsync();

    Task<IReadOnlyList<ReviewRequestInfo>> GetReviewRequestsAsync();
    Task<IReadOnlyList<PullRequestInfo>> GetOpenPullRequestsAsync();
    Task<IReadOnlyList<CiRunInfo>> GetLatestCiRunsAsync();
    Task<IReadOnlyList<NotificationInfo>> GetNotificationsAsync();
    Task<IReadOnlyList<IssueInfo>> GetAssignedIssuesAsync();
    Task<IReadOnlyList<AnnotationInfo>> GetAnnotationsForRunAsync(string repo, long checkSuiteId);
    Task<IReadOnlyList<PrCommentInfo>> GetPrCommentsAsync(string repo, int prNumber);

    RateLimitInfo? LastRateLimit { get; }
    TokenScopeWarning? CheckTokenScopes();
}
