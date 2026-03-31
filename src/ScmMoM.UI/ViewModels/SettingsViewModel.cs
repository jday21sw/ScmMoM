using System.Collections.ObjectModel;
using System.Reactive;
using Avalonia;
using ScmMoM.Core.Models;
using ScmMoM.Core.Services;
using ReactiveUI;

namespace ScmMoM.UI.ViewModels;

public class AccountSettingsItem : ReactiveObject
{
    public string AccountId { get; init; } = string.Empty;

    private string _displayName = string.Empty;
    public string DisplayName { get => _displayName; set => this.RaiseAndSetIfChanged(ref _displayName, value); }

    private string _providerType = string.Empty;
    public string ProviderType { get => _providerType; set => this.RaiseAndSetIfChanged(ref _providerType, value); }

    private string _organization = string.Empty;
    public string Organization { get => _organization; set => this.RaiseAndSetIfChanged(ref _organization, value); }

    private string _repositoriesCsv = string.Empty;
    public string RepositoriesCsv { get => _repositoriesCsv; set => this.RaiseAndSetIfChanged(ref _repositoriesCsv, value); }

    private string _serverUrl = string.Empty;
    public string ServerUrl { get => _serverUrl; set => this.RaiseAndSetIfChanged(ref _serverUrl, value); }

    public ReactiveCommand<Unit, Unit>? RemoveCommand { get; init; }
}

public class SettingsViewModel : ReactiveObject
{
    private readonly ConfigService _configService;

    public ObservableCollection<AccountSettingsItem> AccountSettings { get; } = new();

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

    private string _apiPsk = string.Empty;
    public string ApiPsk
    {
        get => _apiPsk;
        set => this.RaiseAndSetIfChanged(ref _apiPsk, value);
    }

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> GeneratePskCommand { get; }

    private readonly ITokenStore? _tokenStore;
    private readonly AccountManager? _accountManager;

    public SettingsViewModel(ConfigService configService, ITokenStore? tokenStore = null, AccountManager? accountManager = null)
    {
        _configService = configService;
        _tokenStore = tokenStore;
        _accountManager = accountManager;
        var cfg = configService.Config;

        // Load all account settings
        foreach (var account in cfg.Accounts)
        {
            AccountSettings.Add(new AccountSettingsItem
            {
                AccountId = account.Id,
                DisplayName = account.DisplayName,
                ProviderType = account.ProviderType.ToString(),
                Organization = account.Organization,
                RepositoriesCsv = string.Join(", ", account.Repositories),
                ServerUrl = account.ServerUrl,
                RemoveCommand = ReactiveCommand.Create(() => RemoveAccount(account.Id))
            });
        }

        _refreshIntervalSeconds = cfg.RefreshIntervalSeconds;
        _notificationsEnabled = cfg.NotificationsEnabled;
        _themeMode = cfg.ThemeMode;
        _webServerEnabled = cfg.WebServerEnabled;
        _webServerPort = cfg.WebServerPort;
        _apiPsk = cfg.ApiPsk ?? string.Empty;

        SaveCommand = ReactiveCommand.Create(Save);
        GeneratePskCommand = ReactiveCommand.Create(GeneratePsk);
    }

    private void GeneratePsk()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        ApiPsk = Convert.ToBase64String(bytes);
    }

    private void RemoveAccount(string accountId)
    {
        _configService.Config.Accounts.RemoveAll(a => a.Id == accountId);
        _configService.Save();
        _tokenStore?.RemoveToken($"scmmom:{accountId}");
        _accountManager?.RemoveProvider(accountId);

        var item = AccountSettings.FirstOrDefault(a => a.AccountId == accountId);
        if (item != null) AccountSettings.Remove(item);

        StatusMessage = "Account removed. Restart the app to fully apply changes.";
    }

    private void Save()
    {
        if (RefreshIntervalSeconds < 30)
        {
            StatusMessage = "Minimum refresh interval is 30 seconds.";
            return;
        }

        // Validate and save each account
        foreach (var item in AccountSettings)
        {
            var account = _configService.Config.Accounts.FirstOrDefault(a => a.Id == item.AccountId);
            if (account == null) continue;

            var repos = item.RepositoriesCsv
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .ToList();

            if (repos.Count == 0)
            {
                StatusMessage = $"Account '{item.DisplayName}' needs at least one repository.";
                return;
            }

            account.DisplayName = item.DisplayName.Trim();
            account.Organization = item.Organization.Trim();
            account.Repositories = repos;
            account.ServerUrl = item.ServerUrl.Trim();
        }

        _configService.Config.RefreshIntervalSeconds = RefreshIntervalSeconds;
        _configService.Config.NotificationsEnabled = NotificationsEnabled;
        _configService.Config.ThemeMode = ThemeMode;
        _configService.Config.WebServerEnabled = WebServerEnabled;
        _configService.Config.WebServerPort = WebServerPort;
        _configService.Config.ApiPsk = ApiPsk.Trim();
        _configService.Save();

        if (Application.Current is App app)
        {
            app.ApplyTheme(ThemeMode);

            if (WebServerEnabled)
                app.StartWebServer();
            else
                app.StopWebServer();
        }

        StatusMessage = "Settings saved. Changes take effect on next refresh.";
    }
}
