using System.Reactive;
using Avalonia;
using BPM.Core.Services;
using ReactiveUI;

namespace BPM.UI.ViewModels;

public class SettingsViewModel : ReactiveObject
{
    private readonly ConfigService _configService;

    private string _organization;
    public string Organization
    {
        get => _organization;
        set => this.RaiseAndSetIfChanged(ref _organization, value);
    }

    private string _repositoriesCsv;
    public string RepositoriesCsv
    {
        get => _repositoriesCsv;
        set => this.RaiseAndSetIfChanged(ref _repositoriesCsv, value);
    }

    private int _refreshIntervalSeconds;
    public int RefreshIntervalSeconds
    {
        get => _refreshIntervalSeconds;
        set => this.RaiseAndSetIfChanged(ref _refreshIntervalSeconds, value);
    }

    private bool _notificationsEnabled;
    public bool NotificationsEnabled
    {
        get => _notificationsEnabled;
        set => this.RaiseAndSetIfChanged(ref _notificationsEnabled, value);
    }

    private string _themeMode;
    public string ThemeMode
    {
        get => _themeMode;
        set => this.RaiseAndSetIfChanged(ref _themeMode, value);
    }

    private bool _webServerEnabled;
    public bool WebServerEnabled
    {
        get => _webServerEnabled;
        set => this.RaiseAndSetIfChanged(ref _webServerEnabled, value);
    }

    private int _webServerPort;
    public int WebServerPort
    {
        get => _webServerPort;
        set => this.RaiseAndSetIfChanged(ref _webServerPort, value);
    }

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    public ReactiveCommand<Unit, Unit> SaveCommand { get; }

    public SettingsViewModel(ConfigService configService)
    {
        _configService = configService;
        var cfg = configService.Config;

        _organization = cfg.Organization;
        _repositoriesCsv = string.Join(", ", cfg.Repositories);
        _refreshIntervalSeconds = cfg.RefreshIntervalSeconds;
        _notificationsEnabled = cfg.NotificationsEnabled;
        _themeMode = cfg.ThemeMode;
        _webServerEnabled = cfg.WebServerEnabled;
        _webServerPort = cfg.WebServerPort;

        SaveCommand = ReactiveCommand.Create(Save);
    }

    private void Save()
    {
        if (RefreshIntervalSeconds < 30)
        {
            StatusMessage = "Minimum refresh interval is 30 seconds.";
            return;
        }

        var repos = RepositoriesCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .ToList();

        if (repos.Count == 0)
        {
            StatusMessage = "At least one repository is required.";
            return;
        }

        _configService.Config.Organization = Organization.Trim();
        _configService.Config.Repositories = repos;
        _configService.Config.RefreshIntervalSeconds = RefreshIntervalSeconds;
        _configService.Config.NotificationsEnabled = NotificationsEnabled;
        _configService.Config.ThemeMode = ThemeMode;
        _configService.Config.WebServerEnabled = WebServerEnabled;
        _configService.Config.WebServerPort = WebServerPort;
        _configService.Save();

        // Apply theme immediately
        if (Application.Current is App app)
        {
            app.ApplyTheme(ThemeMode);

            // Toggle web server
            if (WebServerEnabled)
                app.StartWebServer();
            else
                app.StopWebServer();
        }

        StatusMessage = "Settings saved. Changes take effect on next refresh.";
    }
}
