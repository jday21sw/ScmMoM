using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Threading;
using ScmMoM.Core.Models;
using ScmMoM.Core.Services;
using ReactiveUI;

namespace ScmMoM.UI.ViewModels;

public class DashboardViewModel : ReactiveObject, IDisposable
{
    private readonly IScmProvider _provider;
    private readonly ConfigService _configService;
    private readonly NotificationService _notificationService;
    private DispatcherTimer? _refreshTimer;
    private bool _isFirstLoad = true;

    public ObservableCollection<ReviewRequestInfo> ReviewRequests { get; } = new();
    public ObservableCollection<PullRequestInfo> OpenPullRequests { get; } = new();
    public ObservableCollection<CiRunInfo> CiRuns { get; } = new();
    public ObservableCollection<NotificationService.NotificationEvent> PendingNotifications { get; } = new();

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

    public DashboardViewModel(IScmProvider provider, ConfigService configService, NotificationService notificationService)
    {
        _provider = provider;
        _configService = configService;
        _notificationService = notificationService;

        Organization = configService.Config.Accounts.FirstOrDefault()?.Organization ?? string.Empty;
        RefreshIntervalSeconds = configService.Config.RefreshIntervalSeconds;

        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshDataAsync);
        OpenUrlCommand = ReactiveCommand.Create<string>(OpenUrl);
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

    private async Task RefreshDataAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var reviewsTask = _provider.GetReviewRequestsAsync();
            var prsTask = _provider.GetOpenPullRequestsAsync();
            var ciRunsTask = _provider.GetLatestCiRunsAsync();

            await Task.WhenAll(reviewsTask, prsTask, ciRunsTask);

            var reviews = await reviewsTask;
            var prs = await prsTask;
            var ciRuns = await ciRunsTask;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ReviewRequests.Clear();
                foreach (var r in reviews) ReviewRequests.Add(r);

                OpenPullRequests.Clear();
                foreach (var pr in prs) OpenPullRequests.Add(pr);

                CiRuns.Clear();
                foreach (var a in ciRuns) CiRuns.Add(a);

                // Hide detail panels on refresh so stale data isn't shown
                IsActionDetailVisible = false;
                IsPrDetailVisible = false;
            });

            // Update rate limit display
            if (_provider.LastRateLimit is { } rl)
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
                var newReviews = _notificationService.DetectNewReviewRequests(reviews);
                var newFailures = _notificationService.DetectFailedCiRuns(ciRuns);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (var n in newReviews) PendingNotifications.Add(n);
                    foreach (var n in newFailures) PendingNotifications.Add(n);
                });
            }
            else if (_isFirstLoad)
            {
                // Populate baseline for diff detection
                _notificationService.DetectNewReviewRequests(reviews);
                _notificationService.DetectFailedCiRuns(ciRuns);
                _isFirstLoad = false;
            }

            // Update tray tooltip
            var failedCount = ciRuns.Count(a => a.Conclusion == "failure");
            TrayTooltip = $"ScmMoM \u2014 {reviews.Count} review requests, {failedCount} failed CI runs";
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
        IsActionDetailVisible = true;
        IsActionDetailLoading = true;
        HasNoAnnotations = false;
        ActionDetailHeader = $"Annotations — {run.WorkflowName} #{run.RunNumber} ({run.RepoName})";
        Annotations.Clear();

        try
        {
            var annotations = await _provider.GetAnnotationsForRunAsync(run.RepoName, run.CheckSuiteId);
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
        IsPrDetailVisible = true;
        IsPrDetailLoading = true;
        PrDetailHeader = $"Comments — {pr.RepoName} #{pr.Number}: {pr.Title}";
        PrComments.Clear();

        try
        {
            var comments = await _provider.GetPrCommentsAsync(pr.RepoName, pr.Number);
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
