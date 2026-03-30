using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Threading;
using ScmMoM.Core.Models;
using ScmMoM.Core.Services;
using ReactiveUI;

namespace ScmMoM.UI.ViewModels;

/// <summary>Represents an account entry in the sidebar.</summary>
public class AccountItemViewModel : ReactiveObject
{
    public string AccountId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string ProviderIcon { get; init; } = string.Empty; // emoji icon
    public string ProviderType { get; init; } = string.Empty;

    private string _statusDot = "🟢";
    public string StatusDot { get => _statusDot; set => this.RaiseAndSetIfChanged(ref _statusDot, value); }
}

public class DashboardViewModel : ReactiveObject, IDisposable
{
    private readonly AccountManager _accountManager;
    private readonly ConfigService _configService;
    private readonly NotificationService _notificationService;
    private DispatcherTimer? _refreshTimer;
    private bool _isFirstLoad = true;

    public ObservableCollection<ReviewRequestInfo> ReviewRequests { get; } = new();
    public ObservableCollection<PullRequestInfo> OpenPullRequests { get; } = new();
    public ObservableCollection<CiRunInfo> CiRuns { get; } = new();
    public ObservableCollection<NotificationInfo> Notifications { get; } = new();
    public ObservableCollection<IssueInfo> Issues { get; } = new();
    public ObservableCollection<NotificationService.NotificationEvent> PendingNotifications { get; } = new();

    // Sidebar account list
    public ObservableCollection<AccountItemViewModel> AccountItems { get; } = new();

    // Detail panel collections
    public ObservableCollection<AnnotationInfo> Annotations { get; } = new();
    public ObservableCollection<PrCommentInfo> PrComments { get; } = new();

    private string _username = string.Empty;
    public string Username
    {
        get => _username;
        set => this.RaiseAndSetIfChanged(ref _username, value);
    }

    private string _organization = string.Empty;
    public string Organization
    {
        get => _organization;
        set => this.RaiseAndSetIfChanged(ref _organization, value);
    }

    private string _lastRefreshText = "Never";
    public string LastRefreshText
    {
        get => _lastRefreshText;
        set => this.RaiseAndSetIfChanged(ref _lastRefreshText, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }

    private string _errorMessage = string.Empty;
    public string ErrorMessage
    {
        get => _errorMessage;
        set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    private int _rateLimitRemaining;
    public int RateLimitRemaining
    {
        get => _rateLimitRemaining;
        set => this.RaiseAndSetIfChanged(ref _rateLimitRemaining, value);
    }

    private int _rateLimitTotal;
    public int RateLimitTotal
    {
        get => _rateLimitTotal;
        set => this.RaiseAndSetIfChanged(ref _rateLimitTotal, value);
    }

    private string _rateLimitResetText = string.Empty;
    public string RateLimitResetText
    {
        get => _rateLimitResetText;
        set => this.RaiseAndSetIfChanged(ref _rateLimitResetText, value);
    }

    private string _rateLimitColor = "Green";
    public string RateLimitColor
    {
        get => _rateLimitColor;
        set => this.RaiseAndSetIfChanged(ref _rateLimitColor, value);
    }

    private int _refreshIntervalSeconds;
    public int RefreshIntervalSeconds
    {
        get => _refreshIntervalSeconds;
        set => this.RaiseAndSetIfChanged(ref _refreshIntervalSeconds, value);
    }

    private string _trayTooltip = "ScmMoM — Source Code Monitor of Monitors";
    public string TrayTooltip
    {
        get => _trayTooltip;
        set => this.RaiseAndSetIfChanged(ref _trayTooltip, value);
    }
    private string _scopeWarning = string.Empty;
    public string ScopeWarning
    {
        get => _scopeWarning;
        set => this.RaiseAndSetIfChanged(ref _scopeWarning, value);
    }
    // --- Action Detail Panel ---
    private bool _isActionDetailVisible;
    public bool IsActionDetailVisible
    {
        get => _isActionDetailVisible;
        set => this.RaiseAndSetIfChanged(ref _isActionDetailVisible, value);
    }

    private bool _isActionDetailLoading;
    public bool IsActionDetailLoading
    {
        get => _isActionDetailLoading;
        set => this.RaiseAndSetIfChanged(ref _isActionDetailLoading, value);
    }

    private string _actionDetailHeader = string.Empty;
    public string ActionDetailHeader
    {
        get => _actionDetailHeader;
        set => this.RaiseAndSetIfChanged(ref _actionDetailHeader, value);
    }

    private bool _hasNoAnnotations;
    public bool HasNoAnnotations
    {
        get => _hasNoAnnotations;
        set => this.RaiseAndSetIfChanged(ref _hasNoAnnotations, value);
    }

    // --- PR Detail Panel ---
    private bool _isPrDetailVisible;
    public bool IsPrDetailVisible
    {
        get => _isPrDetailVisible;
        set => this.RaiseAndSetIfChanged(ref _isPrDetailVisible, value);
    }

    private bool _isPrDetailLoading;
    public bool IsPrDetailLoading
    {
        get => _isPrDetailLoading;
        set => this.RaiseAndSetIfChanged(ref _isPrDetailLoading, value);
    }

    private string _prDetailHeader = string.Empty;
    public string PrDetailHeader
    {
        get => _prDetailHeader;
        set => this.RaiseAndSetIfChanged(ref _prDetailHeader, value);
    }

    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ReactiveCommand<string, Unit> OpenUrlCommand { get; }

    /// <summary>The currently selected provider for detail panel operations (annotations, comments).</summary>
    public IScmProvider? ActiveProvider => _accountManager.ActiveProvider;

    public DashboardViewModel(AccountManager accountManager, ConfigService configService, NotificationService notificationService)
    {
        _accountManager = accountManager;
        _configService = configService;
        _notificationService = notificationService;

        Organization = configService.Config.Accounts.FirstOrDefault()?.Organization ?? string.Empty;
        RefreshIntervalSeconds = configService.Config.RefreshIntervalSeconds;

        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshDataAsync);
        OpenUrlCommand = ReactiveCommand.Create<string>(OpenUrl);

        RebuildAccountItems();
    }

    public void StartAutoRefresh()
    {
        _refreshTimer?.Stop();
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(RefreshIntervalSeconds)
        };
        _refreshTimer.Tick += async (_, _) => await RefreshDataAsync();
        _refreshTimer.Start();
    }

    public void UpdateRefreshInterval(int seconds)
    {
        RefreshIntervalSeconds = seconds;
        _configService.Config.RefreshIntervalSeconds = seconds;
        _configService.Save();
        StartAutoRefresh();
    }

    public void RebuildAccountItems()
    {
        AccountItems.Clear();
        foreach (var kvp in _accountManager.Providers)
        {
            var provider = kvp.Value;
            var config = _configService.Config.Accounts.FirstOrDefault(a => a.Id == provider.AccountId);
            AccountItems.Add(new AccountItemViewModel
            {
                AccountId = provider.AccountId,
                DisplayName = config?.DisplayName ?? provider.AccountId,
                ProviderIcon = provider.ProviderType switch
                {
                    ScmProviderType.GitHub => "🐙",
                    ScmProviderType.GitLab => "🦊",
                    ScmProviderType.Gitea => "🍵",
                    _ => "🔗"
                },
                ProviderType = provider.ProviderType.ToString()
            });
        }
    }

    private async Task RefreshDataAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var allReviews = new List<ReviewRequestInfo>();
            var allPrs = new List<PullRequestInfo>();
            var allCiRuns = new List<CiRunInfo>();
            var allNotifications = new List<NotificationInfo>();
            var allIssues = new List<IssueInfo>();

            // Fetch data from all providers in parallel
            var providers = _accountManager.Providers.Values.ToList();
            var tasks = providers.Select(async provider =>
            {
                try
                {
                    var reviewsTask = provider.GetReviewRequestsAsync();
                    var prsTask = provider.GetOpenPullRequestsAsync();
                    var ciRunsTask = provider.GetLatestCiRunsAsync();
                    var notificationsTask = provider.GetNotificationsAsync();
                    var issuesTask = provider.GetAssignedIssuesAsync();

                    await Task.WhenAll(reviewsTask, prsTask, ciRunsTask, notificationsTask, issuesTask);

                    return (
                        Reviews: (IReadOnlyList<ReviewRequestInfo>)await reviewsTask,
                        Prs: (IReadOnlyList<PullRequestInfo>)await prsTask,
                        CiRuns: (IReadOnlyList<CiRunInfo>)await ciRunsTask,
                        Notifications: (IReadOnlyList<NotificationInfo>)await notificationsTask,
                        Issues: (IReadOnlyList<IssueInfo>)await issuesTask,
                        Provider: provider,
                        Error: (string?)null
                    );
                }
                catch (Exception ex)
                {
                    return (
                        Reviews: (IReadOnlyList<ReviewRequestInfo>)Array.Empty<ReviewRequestInfo>(),
                        Prs: (IReadOnlyList<PullRequestInfo>)Array.Empty<PullRequestInfo>(),
                        CiRuns: (IReadOnlyList<CiRunInfo>)Array.Empty<CiRunInfo>(),
                        Notifications: (IReadOnlyList<NotificationInfo>)Array.Empty<NotificationInfo>(),
                        Issues: (IReadOnlyList<IssueInfo>)Array.Empty<IssueInfo>(),
                        Provider: provider,
                        Error: (string?)ex.Message
                    );
                }
            });

            var results = await Task.WhenAll(tasks);

            foreach (var result in results)
            {
                allReviews.AddRange(result.Reviews);
                allPrs.AddRange(result.Prs);
                allCiRuns.AddRange(result.CiRuns);
                allNotifications.AddRange(result.Notifications);
                allIssues.AddRange(result.Issues);

                // Update sidebar status dot
                var accountItem = AccountItems.FirstOrDefault(a => a.AccountId == result.Provider.AccountId);
                if (accountItem != null)
                    accountItem.StatusDot = result.Error != null ? "🔴" : "🟢";

                if (result.Error != null && providers.Count > 1)
                    ErrorMessage += $"[{result.Provider.AccountId}] {result.Error}\n";
                else if (result.Error != null)
                    ErrorMessage = result.Error;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ReviewRequests.Clear();
                foreach (var r in allReviews.OrderByDescending(r => r.CreatedAt)) ReviewRequests.Add(r);

                OpenPullRequests.Clear();
                foreach (var pr in allPrs.OrderByDescending(p => p.UpdatedAt)) OpenPullRequests.Add(pr);

                CiRuns.Clear();
                foreach (var a in allCiRuns.OrderByDescending(c => c.CreatedAt)) CiRuns.Add(a);

                Notifications.Clear();
                foreach (var n in allNotifications.OrderByDescending(n => n.UpdatedAt)) Notifications.Add(n);

                Issues.Clear();
                foreach (var i in allIssues.OrderByDescending(i => i.UpdatedAt)) Issues.Add(i);

                // Hide detail panels on refresh so stale data isn't shown
                IsActionDetailVisible = false;
                IsPrDetailVisible = false;
            });

            // Update rate limit display — use the first provider that has rate limit info
            var rl = providers.Select(p => p.LastRateLimit).FirstOrDefault(r => r != null);
            if (rl != null)
            {
                RateLimitRemaining = rl.Remaining;
                RateLimitTotal = rl.Limit;
                var resetIn = rl.ResetAt - DateTimeOffset.UtcNow;
                RateLimitResetText = resetIn.TotalMinutes > 1
                    ? $"Resets in {resetIn.Minutes}m {resetIn.Seconds}s"
                    : $"Resets in {Math.Max(0, (int)resetIn.TotalSeconds)}s";

                RateLimitColor = rl.Remaining > 1000 ? "Green" : rl.Remaining > 100 ? "Orange" : "Red";
            }

            LastRefreshText = DateTime.Now.ToString("HH:mm:ss");

            // Detect notifications (skip first load to avoid spam)
            if (!_isFirstLoad && _configService.Config.NotificationsEnabled)
            {
                var newReviews = _notificationService.DetectNewReviewRequests(allReviews);
                var newFailures = _notificationService.DetectFailedCiRuns(allCiRuns);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (var n in newReviews) PendingNotifications.Add(n);
                    foreach (var n in newFailures) PendingNotifications.Add(n);
                });
            }
            else if (_isFirstLoad)
            {
                _notificationService.DetectNewReviewRequests(allReviews);
                _notificationService.DetectFailedCiRuns(allCiRuns);
                _isFirstLoad = false;
            }

            // Update tray tooltip
            var failedCount = allCiRuns.Count(a => a.Conclusion == "failure");
            var accountCount = providers.Count;
            TrayTooltip = $"ScmMoM \u2014 {accountCount} account(s), {allReviews.Count} reviews, {failedCount} failed CI";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Refresh failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        // Validate URL to prevent command injection
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && uri.Scheme != "http"))
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = uri.AbsoluteUri,
            UseShellExecute = true
        });
    }

    public async Task LoadCiRunAnnotationsAsync(CiRunInfo run)
    {
        var provider = ActiveProvider;
        if (provider == null) return;

        IsActionDetailVisible = true;
        IsActionDetailLoading = true;
        HasNoAnnotations = false;
        ActionDetailHeader = $"Annotations — {run.WorkflowName} #{run.RunNumber} ({run.RepoName})";
        Annotations.Clear();

        try
        {
            var annotations = await provider.GetAnnotationsForRunAsync(run.RepoName, run.CheckSuiteId);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var a in annotations) Annotations.Add(a);
                HasNoAnnotations = Annotations.Count == 0;
            });
        }
        catch (Exception ex)
        {
            ActionDetailHeader = $"Annotations — Error: {ex.Message}";
        }
        finally
        {
            IsActionDetailLoading = false;
        }
    }

    public async Task LoadPrCommentsAsync(PullRequestInfo pr)
    {
        var provider = ActiveProvider;
        if (provider == null) return;

        IsPrDetailVisible = true;
        IsPrDetailLoading = true;
        PrDetailHeader = $"Comments — {pr.RepoName} #{pr.Number}: {pr.Title}";
        PrComments.Clear();

        try
        {
            var comments = await provider.GetPrCommentsAsync(pr.RepoName, pr.Number);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var c in comments) PrComments.Add(c);
                if (PrComments.Count == 0)
                    PrDetailHeader = $"Comments — {pr.RepoName} #{pr.Number}: No comments";
            });
        }
        catch (Exception ex)
        {
            PrDetailHeader = $"Comments — Error: {ex.Message}";
        }
        finally
        {
            IsPrDetailLoading = false;
        }
    }

    public void Dispose()
    {
        _refreshTimer?.Stop();
    }
}
