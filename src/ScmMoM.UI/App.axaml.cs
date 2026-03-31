using System;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using ScmMoM.Core.Services;
using ScmMoM.UI.ViewModels;
using ScmMoM.UI.Views;
using ScmMoM.UI.WebServer;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;

namespace ScmMoM.UI;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    private MainWindow? _mainWindow;
    private CompactWindow? _compactWindow;
    private DashboardViewModel? _dashboardVm;
    private ScmWebServer? _webServer;
    private TrayIcon? _trayIcon;

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
            services.AddSingleton<AccountManager>();
            services.AddSingleton<NotificationService>();
            services.AddSingleton<ITokenStore, CredentialStoreService>();
            Services = services.BuildServiceProvider();

            // Apply saved theme
            var configService = Services.GetRequiredService<ConfigService>();
            ApplyTheme(configService.Config.ThemeMode);

            // Show login window as the initial main window
            var accountManager = Services.GetRequiredService<AccountManager>();
            var tokenStore = Services.GetRequiredService<ITokenStore>();
            var loginVm = new LoginViewModel(accountManager, tokenStore, configService);
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

        var accountManager = Services.GetRequiredService<AccountManager>();
        var configService = Services.GetRequiredService<ConfigService>();
        var notificationService = Services.GetRequiredService<NotificationService>();
        _dashboardVm = new DashboardViewModel(accountManager, configService, notificationService)
        {
            Username = authenticatedUsername
        };

        // Check token scopes for excessive permissions across all accounts
        var scopeMessages = new List<string>();
        foreach (var kvp in accountManager.Providers)
        {
            var prov = kvp.Value;
            var scopeWarning = prov.CheckTokenScopes();
            if (scopeWarning != null)
            {
                var acctConfig = configService.Config.Accounts.FirstOrDefault(a => a.Id == prov.AccountId);
                var name = acctConfig?.DisplayName ?? prov.AccountId;
                scopeMessages.Add($"{name}: {scopeWarning.Message}");
            }
        }
        if (scopeMessages.Count > 0)
        {
            _dashboardVm.ScopeWarning = $"\u26a0 {string.Join(" | ", scopeMessages)}";
        }

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
        var accountManager = Services.GetRequiredService<AccountManager>();
        var port = configService.Config.WebServerPort;
        var psk = configService.Config.ApiPsk;

        _webServer?.Stop();
        _webServer = new ScmWebServer(_dashboardVm, accountManager, port, psk);
        _webServer.Start();
    }

    public void StopWebServer()
    {
        _webServer?.Stop();
        _webServer = null;
    }

    public void QuitApplication()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;

        _webServer?.Stop();
        _dashboardVm?.Dispose();
        _trayIcon?.Dispose();
        desktop.Shutdown();
    }

    private void SetupTrayIcon(IClassicDesktopStyleApplicationLifetime desktop, DashboardViewModel dashboardVm)
    {
        _trayIcon = new TrayIcon();
        _trayIcon.ToolTipText = "ScmMoM — Source Code Monitor of Monitors";

        // Track tooltip changes
        dashboardVm.ObservableForProperty(x => x.TrayTooltip)
            .Subscribe(change =>
            {
                _trayIcon.ToolTipText = change.Value;
            });

        _trayIcon.Clicked += (_, _) =>
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
        quitItem.Click += (_, _) => QuitApplication();
        menu.Add(quitItem);

        _trayIcon.Menu = menu;

        var trayIcons = new TrayIcons { _trayIcon };
        SetValue(TrayIcon.IconsProperty, trayIcons);
    }
}
