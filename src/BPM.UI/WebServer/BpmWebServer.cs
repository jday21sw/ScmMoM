using System.Text.Json;
using BPM.Core.Services;
using BPM.UI.ViewModels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace BPM.UI.WebServer;

public class BpmWebServer
{
    private readonly DashboardViewModel _vm;
    private readonly IGitHubService _gitHubService;
    private readonly int _port;
    private WebApplication? _app;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public BpmWebServer(DashboardViewModel vm, IGitHubService gitHubService, int port)
    {
        _vm = vm;
        _gitHubService = gitHubService;
        _port = port;
    }

    public void Start()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls($"http://localhost:{_port}");
        builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning);

        _app = builder.Build();

        // Serve static files from wwwroot
        var wwwrootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (Directory.Exists(wwwrootPath))
        {
            _app.UseDefaultFiles(new DefaultFilesOptions
            {
                FileProvider = new PhysicalFileProvider(wwwrootPath)
            });
            _app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(wwwrootPath)
            });
        }

        // API endpoints
        _app.MapGet("/api/status", () => Results.Json(new
        {
            username = _vm.Username,
            organization = _vm.Organization,
            lastRefresh = _vm.LastRefreshText,
            isLoading = _vm.IsLoading,
            rateLimitRemaining = _vm.RateLimitRemaining,
            rateLimitTotal = _vm.RateLimitTotal,
            rateLimitResetText = _vm.RateLimitResetText,
            rateLimitColor = _vm.RateLimitColor,
            reviewCount = _vm.ReviewRequests.Count,
            prCount = _vm.OpenPullRequests.Count,
            actionCount = _vm.ActionRuns.Count
        }));

        _app.MapGet("/api/reviews", () =>
            Results.Json(_vm.ReviewRequests.ToList(), _jsonOptions));

        _app.MapGet("/api/pull-requests", () =>
            Results.Json(_vm.OpenPullRequests.ToList(), _jsonOptions));

        _app.MapGet("/api/actions", () =>
            Results.Json(_vm.ActionRuns.ToList(), _jsonOptions));

        _app.MapGet("/api/actions/{repo}/{checkSuiteId:long}/annotations", async (string repo, long checkSuiteId) =>
        {
            try
            {
                var annotations = await _gitHubService.GetAnnotationsForRunAsync(repo, checkSuiteId);
                return Results.Json(annotations, _jsonOptions);
            }
            catch
            {
                return Results.Json(Array.Empty<object>(), _jsonOptions);
            }
        });

        _app.MapGet("/api/pull-requests/{repo}/{number:int}/comments", async (string repo, int number) =>
        {
            try
            {
                var comments = await _gitHubService.GetPrCommentsAsync(repo, number);
                return Results.Json(comments, _jsonOptions);
            }
            catch
            {
                return Results.Json(Array.Empty<object>(), _jsonOptions);
            }
        });

        Task.Run(() => _app.RunAsync());
    }

    public void Stop()
    {
        var app = _app;
        _app = null;
        if (app == null) return;
        Task.Run(async () =>
        {
            await app.StopAsync();
            await app.DisposeAsync();
        });
    }
}
