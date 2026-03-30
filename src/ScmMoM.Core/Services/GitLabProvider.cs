using System.Net.Http.Json;
using System.Text.Json;
using NGitLab;
using NGitLab.Models;
using ScmMoM.Core.Models;

namespace ScmMoM.Core.Services;

public class GitLabProvider : IScmProvider
{
    private IGitLabClient? _client;
    private string _username = string.Empty;
    private string _token = string.Empty;
    private readonly ScmAccountConfig _account;
    private readonly HttpClient _httpClient = new();

    public string AccountId => _account.Id;
    public ScmProviderType ProviderType => ScmProviderType.GitLab;
    public RateLimitInfo? LastRateLimit { get; private set; }

    private string BaseUrl => string.IsNullOrWhiteSpace(_account.ServerUrl)
        ? "https://gitlab.com"
        : _account.ServerUrl.TrimEnd('/');

    public GitLabProvider(ScmAccountConfig account)
    {
        _account = account;
    }

    public void Initialize(string token, string username)
    {
        _token = token;
        _username = username;
        _client = new GitLabClient(BaseUrl, token);
        _httpClient.BaseAddress = new Uri(BaseUrl);
        _httpClient.DefaultRequestHeaders.Add("PRIVATE-TOKEN", token);
    }

    public async Task<string> ValidateTokenAsync()
    {
        if (_client == null) throw new InvalidOperationException("Provider not initialized.");
        var session = _client.Users;
        // NGitLab doesn't have a direct "current user" async — use the REST fallback
        var response = await _httpClient.GetAsync("/api/v4/user");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var login = json.GetProperty("username").GetString() ?? string.Empty;
        _username = login;
        return login;
    }

    public async Task<IReadOnlyList<ReviewRequestInfo>> GetReviewRequestsAsync()
    {
        if (_client == null) throw new InvalidOperationException("Provider not initialized.");
        var results = new List<ReviewRequestInfo>();

        try
        {
            // Use REST API to get MRs where the current user is a reviewer
            var response = await _httpClient.GetAsync("/api/v4/merge_requests?state=opened&scope=all&reviewer_username=" + Uri.EscapeDataString(_username));
            if (!response.IsSuccessStatusCode)
                return results;

            var mrs = await response.Content.ReadFromJsonAsync<JsonElement>();
            foreach (var mr in mrs.EnumerateArray())
            {
                var projectId = mr.GetProperty("project_id").GetInt64();

                // Filter to configured repos if any are specified
                if (_account.Repositories.Count > 0)
                {
                    var repoName = GetRepoName(projectId);
                    if (!_account.Repositories.Any(r =>
                        repoName.Equals(r, StringComparison.OrdinalIgnoreCase)))
                        continue;
                }

                var referencesStr = mr.TryGetProperty("references", out var refs) && refs.TryGetProperty("full", out var full)
                    ? full.GetString() ?? $"project/{projectId}"
                    : GetRepoName(projectId);

                results.Add(new ReviewRequestInfo
                {
                    RepoName = referencesStr,
                    PullRequestNumber = mr.GetProperty("iid").GetInt32(),
                    Title = mr.GetProperty("title").GetString() ?? string.Empty,
                    Author = mr.TryGetProperty("author", out var author) && author.TryGetProperty("username", out var uname)
                        ? uname.GetString() ?? string.Empty
                        : string.Empty,
                    Url = mr.TryGetProperty("web_url", out var webUrl) ? webUrl.GetString() ?? string.Empty : string.Empty,
                    CreatedAt = mr.TryGetProperty("created_at", out var ca) && DateTime.TryParse(ca.GetString(), out var dt)
                        ? dt : DateTime.UtcNow
                });
            }
        }
        catch { }

        return results.OrderByDescending(r => r.CreatedAt).ToList();
    }

    public async Task<IReadOnlyList<PullRequestInfo>> GetOpenPullRequestsAsync()
    {
        if (_client == null) throw new InvalidOperationException("Provider not initialized.");
        var results = new List<PullRequestInfo>();

        try
        {
            var projectIds = await ResolveProjectIdsAsync();
            foreach (var projectId in projectIds)
            {
                var mrClient = _client.GetMergeRequest(projectId);
                var openMrs = mrClient.AllInState(MergeRequestState.opened).ToList();

                foreach (var mr in openMrs)
                {
                    results.Add(new PullRequestInfo
                    {
                        RepoName = GetRepoName(projectId),
                        Number = (int)mr.Iid,
                        Title = mr.Title ?? string.Empty,
                        State = mr.Draft ? "draft" : "open",
                        Author = mr.Author?.Username ?? string.Empty,
                        Url = mr.WebUrl ?? string.Empty,
                        CreatedAt = mr.CreatedAt,
                        UpdatedAt = mr.UpdatedAt,
                        IsDraft = mr.Draft,
                        Mergeable = mr.HasConflicts ? false : null
                    });
                }
            }
        }
        catch { }

        return results.OrderByDescending(r => r.UpdatedAt).ToList();
    }

    public async Task<IReadOnlyList<CiRunInfo>> GetLatestCiRunsAsync()
    {
        if (_client == null) throw new InvalidOperationException("Provider not initialized.");
        var results = new List<CiRunInfo>();
        var cutoff = DateTimeOffset.UtcNow.AddHours(-48);

        try
        {
            var projectIds = await ResolveProjectIdsAsync();
            foreach (var projectId in projectIds)
            {
                var pipelineClient = _client.GetPipelines(projectId);
                var pipelines = pipelineClient.All.Take(50).ToList();

                var filtered = pipelines
                    .Where(p =>
                    {
                        if (p.Status is JobStatus.Running or JobStatus.Pending or JobStatus.Created or JobStatus.Preparing or JobStatus.WaitingForResource)
                            return true;
                        return p.CreatedAt >= cutoff.UtcDateTime;
                    })
                    .ToList();

                if (filtered.Count == 0 && pipelines.Count > 0)
                {
                    filtered = pipelines
                        .OrderByDescending(p => p.CreatedAt)
                        .Take(5)
                        .ToList();
                }

                var repoName = GetRepoName(projectId);
                foreach (var p in filtered)
                {
                    results.Add(new CiRunInfo
                    {
                        RepoName = repoName,
                        WorkflowName = p.Name ?? "pipeline",
                        Status = MapJobStatus(p.Status),
                        Conclusion = MapConclusion(p.Status),
                        Branch = p.Ref ?? string.Empty,
                        Url = p.WebUrl ?? string.Empty,
                        CreatedAt = p.CreatedAt,
                        RunNumber = p.Id,
                        Event = p.Source ?? string.Empty,
                        Actor = string.Empty, // PipelineBasic doesn't include user info
                        RunId = p.Id,
                        CheckSuiteId = p.Id // GitLab uses pipeline ID
                    });
                }
            }
        }
        catch { }

        return results.OrderByDescending(r => r.CreatedAt).ToList();
    }

    public async Task<IReadOnlyList<Models.NotificationInfo>> GetNotificationsAsync()
    {
        if (_client == null) throw new InvalidOperationException("Provider not initialized.");

        try
        {
            // GitLab "Todos" are the equivalent of notifications
            var response = await _httpClient.GetAsync("/api/v4/todos?state=pending&per_page=50");
            if (!response.IsSuccessStatusCode)
                return Array.Empty<Models.NotificationInfo>();

            var todos = await response.Content.ReadFromJsonAsync<JsonElement>();
            var results = new List<Models.NotificationInfo>();

            foreach (var todo in todos.EnumerateArray())
            {
                results.Add(new Models.NotificationInfo
                {
                    Id = todo.GetProperty("id").GetInt64().ToString(),
                    RepoName = todo.TryGetProperty("project", out var proj) && proj.TryGetProperty("path", out var path)
                        ? path.GetString() ?? string.Empty
                        : string.Empty,
                    Title = todo.TryGetProperty("body", out var body) ? body.GetString() ?? string.Empty : string.Empty,
                    Type = todo.TryGetProperty("target_type", out var tt) ? tt.GetString() ?? string.Empty : string.Empty,
                    Reason = todo.TryGetProperty("action_name", out var action) ? action.GetString() ?? string.Empty : string.Empty,
                    Url = todo.TryGetProperty("target_url", out var url) ? url.GetString() ?? string.Empty : string.Empty,
                    UpdatedAt = todo.TryGetProperty("updated_at", out var dt) && DateTime.TryParse(dt.GetString(), out var parsed)
                        ? parsed
                        : DateTime.UtcNow,
                    IsUnread = true // pending todos are unread
                });
            }

            return results;
        }
        catch
        {
            return Array.Empty<Models.NotificationInfo>();
        }
    }

    public Task<IReadOnlyList<IssueInfo>> GetAssignedIssuesAsync()
    {
        if (_client == null) throw new InvalidOperationException("Provider not initialized.");

        try
        {
            var query = new IssueQuery
            {
                State = IssueState.opened,
                Scope = "assigned_to_me"
            };
            var issues = _client.Issues.Get(query).ToList();

            return Task.FromResult<IReadOnlyList<IssueInfo>>(issues.Select(i => new IssueInfo
            {
                RepoName = i.WebUrl != null ? ExtractRepoFromUrl(i.WebUrl) : string.Empty,
                Number = (int)i.IssueId,
                Title = i.Title ?? string.Empty,
                State = i.State ?? "open",
                Author = i.Author?.Username ?? string.Empty,
                Url = i.WebUrl ?? string.Empty,
                CreatedAt = i.CreatedAt,
                UpdatedAt = i.UpdatedAt,
                Labels = i.Labels?.ToList() ?? new(),
                Assignee = i.Assignee?.Username
            }).ToList());
        }
        catch
        {
            return Task.FromResult<IReadOnlyList<IssueInfo>>(Array.Empty<IssueInfo>());
        }
    }

    public async Task<IReadOnlyList<AnnotationInfo>> GetAnnotationsForRunAsync(string repo, long pipelineId)
    {
        if (_client == null) throw new InvalidOperationException("Provider not initialized.");
        var results = new List<AnnotationInfo>();

        try
        {
            var projectId = await ResolveProjectIdByNameAsync(repo);
            if (projectId == null) return results;

            var jobClient = _client.GetJobs(projectId.Value);
            var jobs = _client.GetPipelines(projectId.Value).GetJobs(pipelineId);

            foreach (var job in jobs)
            {
                if (job.Status is JobStatus.Failed)
                {
                    results.Add(new AnnotationInfo
                    {
                        CheckRunName = job.Name ?? string.Empty,
                        Level = "failure",
                        Message = $"Job '{job.Name}' failed (stage: {job.Stage})",
                        Title = job.Name ?? string.Empty,
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

    public async Task<IReadOnlyList<PrCommentInfo>> GetPrCommentsAsync(string repo, int mrIid)
    {
        if (_client == null) throw new InvalidOperationException("Provider not initialized.");
        var cutoff = DateTimeOffset.UtcNow.AddHours(-48);

        try
        {
            var projectId = await ResolveProjectIdByNameAsync(repo);
            if (projectId == null) return Array.Empty<PrCommentInfo>();

            var mrClient = _client.GetMergeRequest(projectId.Value);
            var comments = mrClient.Comments(mrIid).All.ToList();

            var allComments = comments.Select(c => new PrCommentInfo
            {
                Author = c.Author?.Username ?? string.Empty,
                Body = c.Body ?? string.Empty,
                CreatedAt = c.CreatedAt,
                HtmlUrl = string.Empty // GitLab MR comments don't have direct HTML URLs in the API model
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
        // GitLab: check via /api/v4/personal_access_tokens/self
        try
        {
            var response = _httpClient.GetAsync("/api/v4/personal_access_tokens/self").GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode) return null;

            var json = response.Content.ReadFromJsonAsync<JsonElement>().GetAwaiter().GetResult();
            if (!json.TryGetProperty("scopes", out var scopes)) return null;

            var tokenScopes = scopes.EnumerateArray().Select(s => s.GetString() ?? string.Empty).ToList();

            var dangerousScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "api", "write_repository", "admin_mode", "sudo"
            };

            var requiredScopes = new List<string> { "read_api" };

            var excessive = tokenScopes
                .Where(s => dangerousScopes.Contains(s))
                .ToList();

            if (excessive.Count == 0) return null;

            return new TokenScopeWarning
            {
                ExcessiveScopes = excessive,
                RecommendedScopes = requiredScopes,
                Message = $"Your token has more permissions than needed: {string.Join(", ", excessive)}. Consider creating a token with only: {string.Join(", ", requiredScopes)}"
            };
        }
        catch
        {
            return null;
        }
    }

    // Helpers

    private readonly Dictionary<long, string> _projectNameCache = new();

    private async Task<List<long>> ResolveProjectIdsAsync()
    {
        if (_client == null) return new();
        var ids = new List<long>();

        if (_account.Repositories.Count > 0)
        {
            foreach (var repo in _account.Repositories)
            {
                var id = await ResolveProjectIdByNameAsync(repo);
                if (id.HasValue)
                    ids.Add(id.Value);
            }
        }
        else if (!string.IsNullOrWhiteSpace(_account.Organization))
        {
            // Get all projects in the group
            var groups = _client.Groups.Search(_account.Organization);
            foreach (var group in groups)
            {
                var projects = _client.Groups[group.Id].Projects;
                foreach (var project in projects)
                {
                    ids.Add(project.Id);
                    _projectNameCache[project.Id] = project.Path;
                }
            }
        }

        return ids;
    }

    private async Task<long?> ResolveProjectIdByNameAsync(string repoName)
    {
        if (_client == null) return null;
        try
        {
            // Try as namespace/path first
            var namespacedPath = repoName.Contains('/')
                ? repoName
                : $"{_account.Organization}/{repoName}";

            var project = await _client.Projects.GetByNamespacedPathAsync(namespacedPath);
            if (project != null)
            {
                _projectNameCache[project.Id] = project.Path;
                return project.Id;
            }
        }
        catch { }
        return null;
    }

    private string GetRepoName(long projectId)
    {
        if (_projectNameCache.TryGetValue(projectId, out var name))
            return name;

        try
        {
            var project = _client!.Projects[projectId];
            _projectNameCache[projectId] = project.Path;
            return project.Path;
        }
        catch
        {
            return $"project/{projectId}";
        }
    }

    private static string MapJobStatus(JobStatus status) => status switch
    {
        JobStatus.Running => "in_progress",
        JobStatus.Pending => "queued",
        JobStatus.Created => "queued",
        JobStatus.Preparing => "queued",
        JobStatus.WaitingForResource => "waiting",
        JobStatus.Manual => "waiting",
        _ => "completed"
    };

    private static string? MapConclusion(JobStatus status) => status switch
    {
        JobStatus.Success => "success",
        JobStatus.Failed => "failure",
        JobStatus.Canceled or JobStatus.Canceling => "cancelled",
        JobStatus.Skipped => "skipped",
        JobStatus.Running or JobStatus.Pending or JobStatus.Created
            or JobStatus.Preparing or JobStatus.WaitingForResource or JobStatus.Manual => null,
        _ => status.ToString().ToLowerInvariant()
    };

    private static string ExtractRepoFromUrl(string url)
    {
        // Extract project path from URL like "https://gitlab.com/org/repo/-/issues/1"
        try
        {
            var uri = new Uri(url);
            var segments = uri.AbsolutePath.Split("/-/", 2);
            var path = segments[0].Trim('/');
            var parts = path.Split('/');
            return parts.Length > 0 ? parts[^1] : path;
        }
        catch
        {
            return string.Empty;
        }
    }
}
