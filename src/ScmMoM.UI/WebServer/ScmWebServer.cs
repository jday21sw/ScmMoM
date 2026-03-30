using System.Text.Json;
using ScmMoM.Core.Services;
using ScmMoM.UI.ViewModels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace ScmMoM.UI.WebServer;

public class ScmWebServer
{
    private readonly DashboardViewModel _vm;
    private readonly AccountManager _accountManager;
    private readonly int _port;
    private readonly string? _apiPsk;
    private WebApplication? _app;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public ScmWebServer(DashboardViewModel vm, AccountManager accountManager, int port, string? apiPsk = null)
    {
        _vm = vm;
        _accountManager = accountManager;
        _port = port;
        _apiPsk = apiPsk;
    }

    public void Start()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls($"http://localhost:{_port}");
        builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning);

        _app = builder.Build();

        // PSK authentication middleware
        if (!string.IsNullOrWhiteSpace(_apiPsk))
        {
            _app.Use(async (context, next) =>
            {
                // Allow static files without auth
                if (!context.Request.Path.StartsWithSegments("/api"))
                {
                    await next();
                    return;
                }

                var apiKey = context.Request.Headers["X-API-Key"].FirstOrDefault();
                if (!string.Equals(apiKey, _apiPsk, StringComparison.Ordinal))
                {
                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsync("Unauthorized: Invalid or missing X-API-Key header");
                    return;
                }

                await next();
            });
        }

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
            lastRefresh = _vm.LastRefreshText,
            isLoading = _vm.IsLoading,
            rateLimitRemaining = _vm.RateLimitRemaining,
            rateLimitTotal = _vm.RateLimitTotal,
            rateLimitResetText = _vm.RateLimitResetText,
            rateLimitColor = _vm.RateLimitColor,
            reviewCount = _vm.ReviewRequests.Count,
            prCount = _vm.OpenPullRequests.Count,
            actionCount = _vm.CiRuns.Count,
            notificationCount = _vm.Notifications.Count,
            issueCount = _vm.Issues.Count,
            accountCount = _vm.AccountItems.Count
        }));

        _app.MapGet("/api/reviews", () =>
            Results.Json(_vm.ReviewRequests.ToList(), _jsonOptions));

        _app.MapGet("/api/pull-requests", () =>
            Results.Json(_vm.OpenPullRequests.ToList(), _jsonOptions));

        _app.MapGet("/api/actions", () =>
            Results.Json(_vm.CiRuns.ToList(), _jsonOptions));

        _app.MapGet("/api/notifications", () =>
            Results.Json(_vm.Notifications.ToList(), _jsonOptions));

        _app.MapGet("/api/issues", () =>
            Results.Json(_vm.Issues.ToList(), _jsonOptions));

        _app.MapGet("/api/accounts", () =>
            Results.Json(_vm.AccountItems.Select(a => new
            {
                a.AccountId,
                a.DisplayName,
                a.ProviderType,
                a.StatusDot
            }).ToList(), _jsonOptions));

        _app.MapGet("/api/actions/{repo}/{checkSuiteId:long}/annotations", async (string repo, long checkSuiteId) =>
        {
            try
            {
                var provider = _accountManager.ActiveProvider;
                if (provider == null) return Results.Json(Array.Empty<object>(), _jsonOptions);
                var annotations = await provider.GetAnnotationsForRunAsync(repo, checkSuiteId);
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
                var provider = _accountManager.ActiveProvider;
                if (provider == null) return Results.Json(Array.Empty<object>(), _jsonOptions);
                var comments = await provider.GetPrCommentsAsync(repo, number);
                return Results.Json(comments, _jsonOptions);
            }
            catch
            {
                return Results.Json(Array.Empty<object>(), _jsonOptions);
            }
        });

        _app.MapPost("/api/refresh", () =>
        {
            try
            {
                _vm.RefreshCommand.Execute().Subscribe();
                return Results.Ok(new { message = "Refresh triggered" });
            }
            catch
            {
                return Results.StatusCode(500);
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
