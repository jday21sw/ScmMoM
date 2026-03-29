using System;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using BPM.Core.Services;
using BPM.UI.ViewModels;
using BPM.UI.Views;
using BPM.UI.WebServer;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;

namespace BPM.UI;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    private MainWindow? _mainWindow;
    private CompactWindow? _compactWindow;
    private DashboardViewModel? _dashboardVm;
    private BpmWebServer? _webServer;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Build DI container
            var configPath = System.IO.Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            var services = new ServiceCollection();
            services.AddSingleton(new ConfigService(configPath));
            services.AddSingleton<IGitHubService, GitHubService>();
            services.AddSingleton<NotificationService>();
            Services = services.BuildServiceProvider();

            // Apply saved theme
            var configService = Services.GetRequiredService<ConfigService>();
            ApplyTheme(configService.Config.ThemeMode);

            // Show login window as the initial main window
            var gitHubService = Services.GetRequiredService<IGitHubService>();
            var loginVm = new LoginViewModel(gitHubService);
            var loginWindow = new LoginWindow { DataContext = loginVm };

            loginWindow.Closed += (_, _) =>
            {
                // If login window closes without a successful result, shut down
                if (_mainWindow == null)
                {
                    desktop.Shutdown();
                }
            };

            desktop.MainWindow = loginWindow;
            loginWindow.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void ApplyTheme(string mode)
    {
        RequestedThemeVariant = mode switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    public void OnLoginSucceeded(string authenticatedUsername)
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;

        var gitHubService = Services.GetRequiredService<IGitHubService>();
        var configService = Services.GetRequiredService<ConfigService>();
        var notificationService = Services.GetRequiredService<NotificationService>();
        _dashboardVm = new DashboardViewModel(gitHubService, configService, notificationService)
        {
            Username = authenticatedUsername
        };

        _mainWindow = new MainWindow { DataContext = _dashboardVm };
        desktop.MainWindow = _mainWindow;

        // Setup tray icon
        SetupTrayIcon(desktop, _dashboardVm);

        // Don't shut down when main window is hidden (tray mode)
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _mainWindow.Show();

        // Start auto-refresh and initial load
        _dashboardVm.StartAutoRefresh();
        _dashboardVm.RefreshCommand.Execute().Subscribe();

        // Start web server if enabled
        if (configService.Config.WebServerEnabled)
        {
            StartWebServer();
        }
    }

    public void ShowCompactMode()
    {
        if (_dashboardVm == null) return;

        _mainWindow?.Hide();

        if (_compactWindow == null)
        {
            _compactWindow = new CompactWindow { DataContext = _dashboardVm };
            _compactWindow.Closed += (_, _) => _compactWindow = null;
        }

        _compactWindow.Show();
        _compactWindow.Activate();
    }

    public void ShowFullWindow()
    {
        _compactWindow?.Hide();

        if (_mainWindow != null)
        {
            _mainWindow.Show();
            _mainWindow.Activate();
        }
    }

    public void StartWebServer()
    {
        if (_dashboardVm == null) return;
        var configService = Services.GetRequiredService<ConfigService>();
        var gitHubService = Services.GetRequiredService<IGitHubService>();
        var port = configService.Config.WebServerPort;

        _webServer?.Stop();
        _webServer = new BpmWebServer(_dashboardVm, gitHubService, port);
        _webServer.Start();
    }

    public void StopWebServer()
    {
        _webServer?.Stop();
        _webServer = null;
    }

    private void SetupTrayIcon(IClassicDesktopStyleApplicationLifetime desktop, DashboardViewModel dashboardVm)
    {
        var trayIcon = new TrayIcon();
        trayIcon.ToolTipText = "BPM — Browser Page Monitor";

        // Track tooltip changes
        dashboardVm.ObservableForProperty(x => x.TrayTooltip)
            .Subscribe(change =>
            {
                trayIcon.ToolTipText = change.Value;
            });

        trayIcon.Clicked += (_, _) =>
        {
            if (_mainWindow != null)
            {
                _mainWindow.Show();
                _mainWindow.Activate();
            }
        };

        var menu = new NativeMenu();

        var openItem = new NativeMenuItem("Open Dashboard");
        openItem.Click += (_, _) => ShowFullWindow();
        menu.Add(openItem);

        var compactItem = new NativeMenuItem("Compact Mode");
        compactItem.Click += (_, _) => ShowCompactMode();
        menu.Add(compactItem);

        var refreshItem = new NativeMenuItem("Refresh Now");
        refreshItem.Click += (_, _) =>
        {
            dashboardVm.RefreshCommand.Execute().Subscribe();
        };
        menu.Add(refreshItem);

        menu.Add(new NativeMenuItemSeparator());

        var quitItem = new NativeMenuItem("Quit");
        quitItem.Click += (_, _) =>
        {
            _webServer?.Stop();
            dashboardVm.Dispose();
            trayIcon.Dispose();
            desktop.Shutdown();
        };
        menu.Add(quitItem);

        trayIcon.Menu = menu;

        var trayIcons = new TrayIcons { trayIcon };
        SetValue(TrayIcon.IconsProperty, trayIcons);
    }
}
