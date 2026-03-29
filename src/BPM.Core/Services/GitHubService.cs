using BPM.Core.Models;
using Octokit;

namespace BPM.Core.Services;

public class GitHubService : IGitHubService
{
    private GitHubClient? _client;
    private string _username = string.Empty;
    private readonly ConfigService _configService;

    public RateLimitInfo? LastRateLimit { get; private set; }

    public GitHubService(ConfigService configService)
    {
        _configService = configService;
    }

    public void Initialize(string token, string username)
    {
        _username = username;
        _client = new GitHubClient(new ProductHeaderValue("BPM-Browser-Page-Monitor"))
        {
            Credentials = new Credentials(token)
        };
    }

    public async Task<string> ValidateTokenAsync()
    {
        if (_client == null) throw new InvalidOperationException("Service not initialized.");
        var user = await _client.User.Current();
        UpdateRateLimit(_client.GetLastApiInfo());
        return user.Login;
    }

    public async Task<IReadOnlyList<ReviewRequestInfo>> GetMyReviewRequestsAsync()
    {
        if (_client == null) throw new InvalidOperationException("Service not initialized.");
        var config = _configService.Config;
        var results = new List<ReviewRequestInfo>();

        var tasks = config.Repositories.Select(async repo =>
        {
            try
            {
                var request = new PullRequestRequest { State = ItemStateFilter.Open };
                var prs = await _client.PullRequest.GetAllForRepository(config.Organization, repo, request);

                var repoResults = new List<ReviewRequestInfo>();
                foreach (var pr in prs)
                {
                    var reviewRequests = await _client.PullRequest.ReviewRequest.Get(config.Organization, repo, pr.Number);
                    if (reviewRequests.Users.Any(u => u.Login.Equals(_username, StringComparison.OrdinalIgnoreCase)))
                    {
                        repoResults.Add(new ReviewRequestInfo
                        {
                            RepoName = repo,
                            PullRequestNumber = pr.Number,
                            Title = pr.Title,
                            Author = pr.User.Login,
                            Url = pr.HtmlUrl,
                            CreatedAt = pr.CreatedAt.UtcDateTime
                        });
                    }
                }
                return (IEnumerable<ReviewRequestInfo>)repoResults;
            }
            catch (NotFoundException)
            {
                return Enumerable.Empty<ReviewRequestInfo>();
            }
        });

        var allResults = await Task.WhenAll(tasks);
        foreach (var batch in allResults)
            results.AddRange(batch);

        UpdateRateLimit(_client.GetLastApiInfo());
        return results.OrderByDescending(r => r.CreatedAt).ToList();
    }

    public async Task<IReadOnlyList<PullRequestInfo>> GetOpenPullRequestsAsync()
    {
        if (_client == null) throw new InvalidOperationException("Service not initialized.");
        var config = _configService.Config;
        var results = new List<PullRequestInfo>();

        var tasks = config.Repositories.Select(async repo =>
        {
            try
            {
                var request = new PullRequestRequest { State = ItemStateFilter.Open };
                var prs = await _client.PullRequest.GetAllForRepository(config.Organization, repo, request);

                return prs
                    .Where(pr => pr.User?.Login != null)
                    .Select(pr => new PullRequestInfo
                    {
                        RepoName = repo,
                        Number = pr.Number,
                        Title = pr.Title ?? string.Empty,
                        State = pr.Draft ? "draft" : "open",
                        Author = pr.User!.Login,
                        Url = pr.HtmlUrl ?? string.Empty,
                        CreatedAt = pr.CreatedAt.UtcDateTime,
                        UpdatedAt = pr.UpdatedAt.UtcDateTime,
                        IsDraft = pr.Draft,
                        Mergeable = pr.Mergeable
                    })
                    .ToList();
            }
            catch (Exception)
            {
                return Enumerable.Empty<PullRequestInfo>();
            }
        });

        var allResults = await Task.WhenAll(tasks);
        foreach (var batch in allResults)
            results.AddRange(batch);

        UpdateRateLimit(_client.GetLastApiInfo());
        return results.OrderByDescending(r => r.UpdatedAt).ToList();
    }

    public async Task<IReadOnlyList<ActionRunInfo>> GetLatestActionRunsAsync()
    {
        if (_client == null) throw new InvalidOperationException("Service not initialized.");
        var config = _configService.Config;
        var results = new List<ActionRunInfo>();
        var cutoff = DateTimeOffset.UtcNow.AddHours(-48);

        var tasks = config.Repositories.Select(async repo =>
        {
            try
            {
                var runs = await _client.Actions.Workflows.Runs.List(config.Organization, repo);

                return runs.WorkflowRuns
                    .Where(run =>
                    {
                        var status = run.Status.StringValue;
                        // Include pending/queued/in_progress regardless of age
                        if (status is "queued" or "in_progress" or "waiting" or "pending")
                            return true;
                        // Include completed runs only from last 48 hours
                        return run.CreatedAt >= cutoff;
                    })
                    .Select(run => new ActionRunInfo
                    {
                        RepoName = repo,
                        WorkflowName = run.Name ?? string.Empty,
                        Status = run.Status.StringValue,
                        Conclusion = run.Conclusion?.StringValue,
                        Branch = run.HeadBranch ?? string.Empty,
                        Url = run.HtmlUrl ?? string.Empty,
                        CreatedAt = run.CreatedAt.UtcDateTime,
                        RunNumber = run.RunNumber,
                        Event = run.Event ?? string.Empty,
                        Actor = run.TriggeringActor?.Login ?? run.Actor?.Login ?? string.Empty,
                        RunId = run.Id,
                        CheckSuiteId = run.CheckSuiteId
                    })
                    .ToList();
            }
            catch (Exception)
            {
                return Enumerable.Empty<ActionRunInfo>();
            }
        });

        var allResults = await Task.WhenAll(tasks);
        foreach (var batch in allResults)
            results.AddRange(batch);

        UpdateRateLimit(_client.GetLastApiInfo());
        return results.OrderByDescending(r => r.CreatedAt).ToList();
    }

    public async Task<IReadOnlyList<AnnotationInfo>> GetAnnotationsForRunAsync(string repo, long checkSuiteId)
    {
        if (_client == null) throw new InvalidOperationException("Service not initialized.");
        var config = _configService.Config;
        var results = new List<AnnotationInfo>();

        try
        {
            var checkRuns = await _client.Check.Run.GetAllForCheckSuite(config.Organization, repo, checkSuiteId);
            foreach (var cr in checkRuns.CheckRuns)
            {
                var annotations = await _client.Check.Run.GetAllAnnotations(config.Organization, repo, cr.Id);
                foreach (var a in annotations)
                {
                    results.Add(new AnnotationInfo
                    {
                        CheckRunName = cr.Name ?? string.Empty,
                        Level = a.AnnotationLevel?.StringValue ?? "notice",
                        Message = a.Message ?? string.Empty,
                        Title = a.Title ?? string.Empty,
                        Path = a.Path ?? string.Empty,
                        StartLine = a.StartLine,
                        EndLine = a.EndLine
                    });
                }
            }
        }
        catch (Exception)
        {
            // Return whatever we have so far
        }

        UpdateRateLimit(_client.GetLastApiInfo());
        return results;
    }

    public async Task<IReadOnlyList<PrCommentInfo>> GetPrCommentsAsync(string repo, int prNumber)
    {
        if (_client == null) throw new InvalidOperationException("Service not initialized.");
        var config = _configService.Config;
        var cutoff = DateTimeOffset.UtcNow.AddHours(-48);

        try
        {
            // Fetch both issue comments and PR review comments
            var issueCommentsTask = _client.Issue.Comment.GetAllForIssue(config.Organization, repo, prNumber);
            var reviewCommentsTask = _client.PullRequest.ReviewComment.GetAll(config.Organization, repo, prNumber);

            await Task.WhenAll(issueCommentsTask, reviewCommentsTask);

            var issueComments = (await issueCommentsTask)
                .Select(c => new PrCommentInfo
                {
                    Author = c.User?.Login ?? string.Empty,
                    Body = c.Body ?? string.Empty,
                    CreatedAt = c.CreatedAt.UtcDateTime,
                    HtmlUrl = c.HtmlUrl ?? string.Empty
                });

            var reviewComments = (await reviewCommentsTask)
                .Select(c => new PrCommentInfo
                {
                    Author = c.User?.Login ?? string.Empty,
                    Body = c.Body ?? string.Empty,
                    CreatedAt = c.CreatedAt.UtcDateTime,
                    HtmlUrl = c.HtmlUrl ?? string.Empty
                });

            var allComments = issueComments.Concat(reviewComments).ToList();

            var recentComments = allComments
                .Where(c => c.CreatedAt >= cutoff.UtcDateTime)
                .OrderByDescending(c => c.CreatedAt)
                .ToList();

            // If no comments in last 48h, take the most recent one
            if (recentComments.Count == 0 && allComments.Count > 0)
            {
                recentComments = allComments
                    .OrderByDescending(c => c.CreatedAt)
                    .Take(1)
                    .ToList();
            }

            UpdateRateLimit(_client.GetLastApiInfo());
            return recentComments;
        }
        catch (Exception)
        {
            return Array.Empty<PrCommentInfo>();
        }
    }

    private void UpdateRateLimit(ApiInfo? apiInfo)
    {
        if (apiInfo?.RateLimit != null)
        {
            LastRateLimit = new RateLimitInfo
            {
                Remaining = apiInfo.RateLimit.Remaining,
                Limit = apiInfo.RateLimit.Limit,
                ResetAt = apiInfo.RateLimit.Reset
            };
        }
    }
}
