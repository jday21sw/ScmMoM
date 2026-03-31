using ScmMoM.Core.Models;
using Octokit;

namespace ScmMoM.Core.Services;

public class GitHubProvider : IScmProvider
{
    private GitHubClient? _client;
    private string _username = string.Empty;
    private readonly ScmAccountConfig _account;

    public string AccountId => _account.Id;
    public ScmProviderType ProviderType => ScmProviderType.GitHub;
    public RateLimitInfo? LastRateLimit { get; private set; }

    public GitHubProvider(ScmAccountConfig account)
    {
        _account = account;
    }

    public void Initialize(string token, string username)
    {
        _username = username;
        Uri baseAddress = GitHubClient.GitHubApiUrl;
        if (!string.IsNullOrWhiteSpace(_account.ServerUrl))
        {
            var url = _account.ServerUrl.TrimEnd('/');
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                url = "https://" + url;
            if (Uri.TryCreate(url, UriKind.Absolute, out var parsed))
                baseAddress = parsed;
        }

        _client = new GitHubClient(new ProductHeaderValue("ScmMoM-Monitor"), baseAddress)
        {
            Credentials = new Credentials(token)
        };
    }

    public async Task<string> ValidateTokenAsync()
    {
        if (_client == null) throw new InvalidOperationException("Provider not initialized.");
        var user = await _client.User.Current();
        UpdateRateLimit(_client.GetLastApiInfo());
        return user.Login;
    }

    public async Task<IReadOnlyList<ReviewRequestInfo>> GetReviewRequestsAsync()
    {
        if (_client == null) throw new InvalidOperationException("Provider not initialized.");
        var results = new List<ReviewRequestInfo>();

        var tasks = _account.Repositories.Select(async repo =>
        {
            try
            {
                var request = new PullRequestRequest { State = ItemStateFilter.Open };
                var prs = await _client.PullRequest.GetAllForRepository(_account.Organization, repo, request);

                var repoResults = new List<ReviewRequestInfo>();
                foreach (var pr in prs)
                {
                    var reviewRequests = await _client.PullRequest.ReviewRequest.Get(_account.Organization, repo, pr.Number);
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
        if (_client == null) throw new InvalidOperationException("Provider not initialized.");
        var results = new List<PullRequestInfo>();

        var tasks = _account.Repositories.Select(async repo =>
        {
            try
            {
                var request = new PullRequestRequest { State = ItemStateFilter.Open };
                var prs = await _client.PullRequest.GetAllForRepository(_account.Organization, repo, request);

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

    public async Task<IReadOnlyList<CiRunInfo>> GetLatestCiRunsAsync()
    {
        if (_client == null) throw new InvalidOperationException("Provider not initialized.");
        var results = new List<CiRunInfo>();
        var cutoff = DateTimeOffset.UtcNow.AddHours(-48);

        var tasks = _account.Repositories.Select(async repo =>
        {
            try
            {
                var runs = await _client.Actions.Workflows.Runs.List(_account.Organization, repo);

                var filtered = runs.WorkflowRuns
                    .Where(run =>
                    {
                        var status = run.Status.StringValue;
                        if (status is "queued" or "in_progress" or "waiting" or "pending")
                            return true;
                        return run.CreatedAt >= cutoff;
                    })
                    .ToList();

                // Fallback: if no runs pass the 48h filter, take the last 5
                if (filtered.Count == 0 && runs.WorkflowRuns.Count > 0)
                {
                    filtered = runs.WorkflowRuns
                        .OrderByDescending(r => r.CreatedAt)
                        .Take(5)
                        .ToList();
                }

                return filtered
                    .Select(run => new CiRunInfo
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
                return Enumerable.Empty<CiRunInfo>();
            }
        });

        var allResults = await Task.WhenAll(tasks);
        foreach (var batch in allResults)
            results.AddRange(batch);

        UpdateRateLimit(_client.GetLastApiInfo());
        return results.OrderByDescending(r => r.CreatedAt).ToList();
    }

    public async Task<IReadOnlyList<Models.NotificationInfo>> GetNotificationsAsync()
    {
        if (_client == null) throw new InvalidOperationException("Provider not initialized.");

        try
        {
            var request = new NotificationsRequest { All = false };
            var notifications = await _client.Activity.Notifications.GetAllForCurrent(request);

            UpdateRateLimit(_client.GetLastApiInfo());
            return notifications.Select(n => new Models.NotificationInfo
            {
                Id = n.Id,
                RepoName = n.Repository?.Name ?? string.Empty,
                Title = n.Subject?.Title ?? string.Empty,
                Type = n.Subject?.Type ?? string.Empty,
                Reason = n.Reason ?? string.Empty,
                Url = n.Subject?.Url ?? string.Empty,
                UpdatedAt = DateTimeOffset.TryParse(n.UpdatedAt, out var dt) ? dt.UtcDateTime : DateTime.UtcNow,
                IsUnread = n.Unread
            }).ToList();
        }
        catch (Exception)
        {
            return Array.Empty<Models.NotificationInfo>();
        }
    }

    public async Task<IReadOnlyList<IssueInfo>> GetAssignedIssuesAsync()
    {
        if (_client == null) throw new InvalidOperationException("Provider not initialized.");

        try
        {
            var request = new IssueRequest
            {
                Filter = IssueFilter.Assigned,
                State = ItemStateFilter.Open
            };
            var issues = await _client.Issue.GetAllForCurrent(request);

            UpdateRateLimit(_client.GetLastApiInfo());
            return issues
                .Where(i => i.PullRequest == null) // Exclude PRs (GitHub returns them as issues too)
                .Select(i => new IssueInfo
                {
                    RepoName = i.Repository?.Name ?? string.Empty,
                    Number = i.Number,
                    Title = i.Title ?? string.Empty,
                    State = i.State.StringValue,
                    Author = i.User?.Login ?? string.Empty,
                    Url = i.HtmlUrl ?? string.Empty,
                    CreatedAt = i.CreatedAt.UtcDateTime,
                    UpdatedAt = i.UpdatedAt?.UtcDateTime ?? i.CreatedAt.UtcDateTime,
                    Labels = i.Labels?.Select(l => l.Name).ToList() ?? new(),
                    Assignee = i.Assignee?.Login
                }).ToList();
        }
        catch (Exception)
        {
            return Array.Empty<IssueInfo>();
        }
    }

    public async Task<IReadOnlyList<AnnotationInfo>> GetAnnotationsForRunAsync(string repo, long checkSuiteId)
    {
        if (_client == null) throw new InvalidOperationException("Provider not initialized.");
        var results = new List<AnnotationInfo>();

        try
        {
            var checkRuns = await _client.Check.Run.GetAllForCheckSuite(_account.Organization, repo, checkSuiteId);
            foreach (var cr in checkRuns.CheckRuns)
            {
                var annotations = await _client.Check.Run.GetAllAnnotations(_account.Organization, repo, cr.Id);
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
        if (_client == null) throw new InvalidOperationException("Provider not initialized.");
        var cutoff = DateTimeOffset.UtcNow.AddHours(-48);

        try
        {
            var issueCommentsTask = _client.Issue.Comment.GetAllForIssue(_account.Organization, repo, prNumber);
            var reviewCommentsTask = _client.PullRequest.ReviewComment.GetAll(_account.Organization, repo, prNumber);

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

    private static readonly HashSet<string> RequiredScopes = new(StringComparer.OrdinalIgnoreCase)
    {
        "repo", "workflow", "read:org"
    };

    private static readonly HashSet<string> DangerousScopes = new(StringComparer.OrdinalIgnoreCase)
    {
        "delete_repo", "admin:org", "admin:repo_hook", "admin:org_hook",
        "write:packages", "gist", "admin:gpg_key", "admin:public_key",
        "admin:ssh_signing_key", "codespace", "admin:enterprise",
        "manage_runners:org", "manage_runners:enterprise", "audit_log",
        "project"
    };

    public TokenScopeWarning? CheckTokenScopes()
    {
        if (_client == null) return null;

        var apiInfo = _client.GetLastApiInfo();
        var scopes = apiInfo?.OauthScopes;

        if (scopes == null || scopes.Count == 0) return null;

        var excessive = scopes
            .Where(s => DangerousScopes.Contains(s))
            .ToList();

        var unnecessary = scopes
            .Where(s => !RequiredScopes.Contains(s) && !DangerousScopes.Contains(s))
            .Where(s => !string.Equals(s, "user", StringComparison.OrdinalIgnoreCase) || scopes.Count > 5)
            .ToList();

        excessive.AddRange(unnecessary);

        if (excessive.Count == 0) return null;

        var recommended = string.Join(", ", RequiredScopes);
        return new TokenScopeWarning
        {
            ExcessiveScopes = excessive,
            RecommendedScopes = RequiredScopes.ToList(),
            Message = $"Your token has more permissions than needed: {string.Join(", ", excessive)}. Consider creating a token with only: {recommended}"
        };
    }
}
