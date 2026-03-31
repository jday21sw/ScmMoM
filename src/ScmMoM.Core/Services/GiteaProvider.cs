using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ScmMoM.Core.Models;

namespace ScmMoM.Core.Services;

public class GiteaProvider : IScmProvider
{
    private readonly ScmAccountConfig _account;
    private HttpClient? _httpClient;
    private string _username = string.Empty;

    public string AccountId => _account.Id;
    public ScmProviderType ProviderType => ScmProviderType.Gitea;
    public RateLimitInfo? LastRateLimit { get; private set; }

    private string BaseUrl
    {
        get
        {
            var url = _account.ServerUrl.TrimEnd('/');
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                url = "https://" + url;
            return url;
        }
    }

    public GiteaProvider(ScmAccountConfig account)
    {
        _account = account;
    }

    public void Initialize(string token, string username)
    {
        _username = username;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(BaseUrl)
        };
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", token);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<string> ValidateTokenAsync()
    {
        var json = await GetJsonAsync("/api/v1/user");
        var login = json.GetProperty("login").GetString() ?? string.Empty;
        _username = login;
        return login;
    }

    public async Task<IReadOnlyList<ReviewRequestInfo>> GetReviewRequestsAsync()
    {
        var results = new List<ReviewRequestInfo>();

        try
        {
            // Get PRs where the user is a requested reviewer
            var repos = await GetRepoListAsync();
            foreach (var (owner, repo) in repos)
            {
                var prs = await GetJsonArrayAsync($"/api/v1/repos/{owner}/{repo}/pulls?state=open&limit=50");
                foreach (var pr in prs)
                {
                    if (pr.TryGetProperty("requested_reviewers", out var reviewers))
                    {
                        foreach (var reviewer in reviewers.EnumerateArray())
                        {
                            if (reviewer.TryGetProperty("login", out var login) &&
                                login.GetString()?.Equals(_username, StringComparison.OrdinalIgnoreCase) == true)
                            {
                                results.Add(new ReviewRequestInfo
                                {
                                    RepoName = repo,
                                    PullRequestNumber = pr.GetProperty("number").GetInt32(),
                                    Title = pr.GetProperty("title").GetString() ?? string.Empty,
                                    Author = pr.TryGetProperty("user", out var user) && user.TryGetProperty("login", out var authorLogin)
                                        ? authorLogin.GetString() ?? string.Empty
                                        : string.Empty,
                                    Url = pr.GetProperty("html_url").GetString() ?? string.Empty,
                                    CreatedAt = pr.TryGetProperty("created_at", out var created) && DateTime.TryParse(created.GetString(), out var dt)
                                        ? dt : DateTime.UtcNow
                                });
                                break;
                            }
                        }
                    }
                }
            }
        }
        catch { }

        return results.OrderByDescending(r => r.CreatedAt).ToList();
    }

    public async Task<IReadOnlyList<PullRequestInfo>> GetOpenPullRequestsAsync()
    {
        var results = new List<PullRequestInfo>();

        try
        {
            var repos = await GetRepoListAsync();
            foreach (var (owner, repo) in repos)
            {
                var prs = await GetJsonArrayAsync($"/api/v1/repos/{owner}/{repo}/pulls?state=open&limit=50");
                foreach (var pr in prs)
                {
                    results.Add(new PullRequestInfo
                    {
                        RepoName = repo,
                        Number = pr.GetProperty("number").GetInt32(),
                        Title = pr.GetProperty("title").GetString() ?? string.Empty,
                        State = "open",
                        Author = pr.TryGetProperty("user", out var user) && user.TryGetProperty("login", out var login)
                            ? login.GetString() ?? string.Empty
                            : string.Empty,
                        Url = pr.GetProperty("html_url").GetString() ?? string.Empty,
                        CreatedAt = TryParseDate(pr, "created_at"),
                        UpdatedAt = TryParseDate(pr, "updated_at"),
                        IsDraft = false, // Gitea API v1 doesn't have a draft field by default
                        Mergeable = pr.TryGetProperty("mergeable", out var m) ? m.GetBoolean() : null
                    });
                }
            }
        }
        catch { }

        return results.OrderByDescending(r => r.UpdatedAt).ToList();
    }

    public async Task<IReadOnlyList<CiRunInfo>> GetLatestCiRunsAsync()
    {
        var results = new List<CiRunInfo>();
        var cutoff = DateTimeOffset.UtcNow.AddHours(-48);

        try
        {
            var repos = await GetRepoListAsync();
            foreach (var (owner, repo) in repos)
            {
                // Gitea Actions API
                var runs = await GetJsonArraySafeAsync($"/api/v1/repos/{owner}/{repo}/actions/runs?limit=50");
                if (runs == null) continue;

                var filtered = new List<JsonElement>();
                foreach (var run in runs)
                {
                    var status = run.TryGetProperty("status", out var s) ? s.GetString() ?? string.Empty : string.Empty;
                    if (status is "queued" or "in_progress" or "waiting" or "running")
                    {
                        filtered.Add(run);
                        continue;
                    }
                    var createdAt = TryParseDate(run, "created_at");
                    if (createdAt >= cutoff.UtcDateTime)
                        filtered.Add(run);
                }

                if (filtered.Count == 0 && runs.Count > 0)
                {
                    filtered = runs
                        .OrderByDescending(r => TryParseDate(r, "created_at"))
                        .Take(5)
                        .ToList();
                }

                foreach (var run in filtered)
                {
                    var status = run.TryGetProperty("status", out var s) ? s.GetString() ?? string.Empty : string.Empty;
                    var conclusion = run.TryGetProperty("conclusion", out var c) ? c.GetString() : null;

                    results.Add(new CiRunInfo
                    {
                        RepoName = repo,
                        WorkflowName = run.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                        Status = status,
                        Conclusion = conclusion,
                        Branch = run.TryGetProperty("head_branch", out var branch) ? branch.GetString() ?? string.Empty : string.Empty,
                        Url = run.TryGetProperty("html_url", out var url) ? url.GetString() ?? string.Empty : string.Empty,
                        CreatedAt = TryParseDate(run, "created_at"),
                        RunNumber = run.TryGetProperty("run_number", out var rn) ? rn.GetInt64() : 0,
                        Event = run.TryGetProperty("event", out var ev) ? ev.GetString() ?? string.Empty : string.Empty,
                        Actor = run.TryGetProperty("triggering_actor", out var actor) && actor.TryGetProperty("login", out var actorLogin)
                            ? actorLogin.GetString() ?? string.Empty
                            : string.Empty,
                        RunId = run.TryGetProperty("id", out var id) ? id.GetInt64() : 0,
                        CheckSuiteId = run.TryGetProperty("id", out var csId) ? csId.GetInt64() : 0
                    });
                }
            }
        }
        catch { }

        return results.OrderByDescending(r => r.CreatedAt).ToList();
    }

    public async Task<IReadOnlyList<Models.NotificationInfo>> GetNotificationsAsync()
    {
        try
        {
            var notifications = await GetJsonArrayAsync("/api/v1/notifications?status-types=unread&limit=50");
            return notifications.Select(n => new Models.NotificationInfo
            {
                Id = n.TryGetProperty("id", out var id) ? id.GetInt64().ToString() : string.Empty,
                RepoName = n.TryGetProperty("repository", out var repo) && repo.TryGetProperty("name", out var rn)
                    ? rn.GetString() ?? string.Empty
                    : string.Empty,
                Title = n.TryGetProperty("subject", out var subj) && subj.TryGetProperty("title", out var title)
                    ? title.GetString() ?? string.Empty
                    : string.Empty,
                Type = n.TryGetProperty("subject", out var subj2) && subj2.TryGetProperty("type", out var type)
                    ? type.GetString() ?? string.Empty
                    : string.Empty,
                Reason = n.TryGetProperty("reason", out var reason) ? reason.GetString() ?? string.Empty : string.Empty,
                Url = n.TryGetProperty("subject", out var subj3) && subj3.TryGetProperty("html_url", out var url)
                    ? url.GetString() ?? string.Empty
                    : string.Empty,
                UpdatedAt = TryParseDate(n, "updated_at"),
                IsUnread = n.TryGetProperty("unread", out var unread) && unread.GetBoolean()
            }).ToList();
        }
        catch
        {
            return Array.Empty<Models.NotificationInfo>();
        }
    }

    public async Task<IReadOnlyList<IssueInfo>> GetAssignedIssuesAsync()
    {
        try
        {
            // Get issues assigned to the current user
            var issues = await GetJsonArrayAsync($"/api/v1/repos/search?limit=0"); // dummy to check — use global issue endpoint
            var assignedIssues = await GetJsonArrayAsync($"/api/v1/repos/search"); // not ideal; use a different endpoint

            // Gitea has no global "assigned to me" issues endpoint before v1.20
            // Use per-repo approach
            var results = new List<IssueInfo>();
            var repos = await GetRepoListAsync();
            foreach (var (owner, repo) in repos)
            {
                var repoIssues = await GetJsonArrayAsync(
                    $"/api/v1/repos/{owner}/{repo}/issues?state=open&type=issues&assigned=true&limit=50");
                foreach (var issue in repoIssues)
                {
                    results.Add(new IssueInfo
                    {
                        RepoName = repo,
                        Number = issue.TryGetProperty("number", out var num) ? num.GetInt32() : 0,
                        Title = issue.GetProperty("title").GetString() ?? string.Empty,
                        State = issue.TryGetProperty("state", out var state) ? state.GetString() ?? "open" : "open",
                        Author = issue.TryGetProperty("user", out var user) && user.TryGetProperty("login", out var login)
                            ? login.GetString() ?? string.Empty
                            : string.Empty,
                        Url = issue.TryGetProperty("html_url", out var url) ? url.GetString() ?? string.Empty : string.Empty,
                        CreatedAt = TryParseDate(issue, "created_at"),
                        UpdatedAt = TryParseDate(issue, "updated_at"),
                        Labels = issue.TryGetProperty("labels", out var labels)
                            ? labels.EnumerateArray()
                                .Where(l => l.TryGetProperty("name", out _))
                                .Select(l => l.GetProperty("name").GetString() ?? string.Empty)
                                .ToList()
                            : new(),
                        Assignee = issue.TryGetProperty("assignee", out var assignee) && assignee.TryGetProperty("login", out var aLogin)
                            ? aLogin.GetString()
                            : null
                    });
                }
            }

            return results;
        }
        catch
        {
            return Array.Empty<IssueInfo>();
        }
    }

    public async Task<IReadOnlyList<AnnotationInfo>> GetAnnotationsForRunAsync(string repo, long runId)
    {
        // Gitea Actions doesn't expose annotations the same way; return job-level info
        var results = new List<AnnotationInfo>();

        try
        {
            var repos = await GetRepoListAsync();
            var match = repos.FirstOrDefault(r => r.repo.Equals(repo, StringComparison.OrdinalIgnoreCase));
            if (match == default) return results;

            var jobs = await GetJsonArraySafeAsync($"/api/v1/repos/{match.owner}/{match.repo}/actions/runs/{runId}/jobs");
            if (jobs == null) return results;

            foreach (var job in jobs)
            {
                var status = job.TryGetProperty("conclusion", out var c) ? c.GetString() : null;
                if (status == "failure")
                {
                    results.Add(new AnnotationInfo
                    {
                        CheckRunName = job.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                        Level = "failure",
                        Message = $"Job '{(job.TryGetProperty("name", out var n2) ? n2.GetString() : "unknown")}' failed",
                        Title = job.TryGetProperty("name", out var n3) ? n3.GetString() ?? string.Empty : string.Empty,
                        Path = string.Empty,
                        StartLine = 0,
                        EndLine = 0
                    });
                }
            }
        }
        catch { }

        return results;
    }

    public async Task<IReadOnlyList<PrCommentInfo>> GetPrCommentsAsync(string repo, int prNumber)
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-48);

        try
        {
            var repos = await GetRepoListAsync();
            var match = repos.FirstOrDefault(r => r.repo.Equals(repo, StringComparison.OrdinalIgnoreCase));
            if (match == default) return Array.Empty<PrCommentInfo>();

            var comments = await GetJsonArrayAsync(
                $"/api/v1/repos/{match.owner}/{match.repo}/issues/{prNumber}/comments?limit=50");

            var allComments = comments.Select(c => new PrCommentInfo
            {
                Author = c.TryGetProperty("user", out var user) && user.TryGetProperty("login", out var login)
                    ? login.GetString() ?? string.Empty
                    : string.Empty,
                Body = c.TryGetProperty("body", out var body) ? body.GetString() ?? string.Empty : string.Empty,
                CreatedAt = TryParseDate(c, "created_at"),
                HtmlUrl = c.TryGetProperty("html_url", out var url) ? url.GetString() ?? string.Empty : string.Empty
            }).ToList();

            var recent = allComments
                .Where(c => c.CreatedAt >= cutoff.UtcDateTime)
                .OrderByDescending(c => c.CreatedAt)
                .ToList();

            if (recent.Count == 0 && allComments.Count > 0)
            {
                recent = allComments
                    .OrderByDescending(c => c.CreatedAt)
                    .Take(1)
                    .ToList();
            }

            return recent;
        }
        catch
        {
            return Array.Empty<PrCommentInfo>();
        }
    }

    public TokenScopeWarning? CheckTokenScopes()
    {
        // Gitea doesn't expose token scopes via API in a straightforward way
        return null;
    }

    // Helpers

    private List<(string owner, string repo)>? _repoListCache;

    private async Task<List<(string owner, string repo)>> GetRepoListAsync()
    {
        if (_repoListCache != null) return _repoListCache;

        var result = new List<(string owner, string repo)>();
        var org = _account.Organization;

        if (_account.Repositories.Count > 0)
        {
            foreach (var repo in _account.Repositories)
            {
                result.Add((org, repo));
            }
        }
        else if (!string.IsNullOrWhiteSpace(org))
        {
            // List all repos in the org
            var repos = await GetJsonArrayAsync($"/api/v1/orgs/{org}/repos?limit=50");
            foreach (var repo in repos)
            {
                var name = repo.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                if (!string.IsNullOrEmpty(name))
                    result.Add((org, name));
            }
        }

        _repoListCache = result;
        return result;
    }

    private async Task<JsonElement> GetJsonAsync(string path)
    {
        if (_httpClient == null) throw new InvalidOperationException("Provider not initialized.");
        var response = await _httpClient.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<List<JsonElement>> GetJsonArrayAsync(string path)
    {
        var json = await GetJsonAsync(path);
        return json.EnumerateArray().ToList();
    }

    private async Task<List<JsonElement>?> GetJsonArraySafeAsync(string path)
    {
        try
        {
            return await GetJsonArrayAsync(path);
        }
        catch
        {
            return null;
        }
    }

    private static DateTime TryParseDate(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var value) && DateTime.TryParse(value.GetString(), out var dt))
            return dt;
        return DateTime.UtcNow;
    }
}
