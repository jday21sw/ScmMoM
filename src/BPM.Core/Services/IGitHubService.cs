using BPM.Core.Models;

namespace BPM.Core.Services;

public interface IGitHubService
{
    void Initialize(string token, string username);
    Task<IReadOnlyList<ReviewRequestInfo>> GetMyReviewRequestsAsync();
    Task<IReadOnlyList<PullRequestInfo>> GetOpenPullRequestsAsync();
    Task<IReadOnlyList<ActionRunInfo>> GetLatestActionRunsAsync();
    Task<string> ValidateTokenAsync();
    Task<IReadOnlyList<AnnotationInfo>> GetAnnotationsForRunAsync(string repo, long checkSuiteId);
    Task<IReadOnlyList<PrCommentInfo>> GetPrCommentsAsync(string repo, int prNumber);
    RateLimitInfo? LastRateLimit { get; }
}
