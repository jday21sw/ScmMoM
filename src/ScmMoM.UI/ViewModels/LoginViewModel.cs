using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using ScmMoM.Core.Models;
using ScmMoM.Core.Services;
using ReactiveUI;

namespace ScmMoM.UI.ViewModels;

public class AccountEntryViewModel : ReactiveObject
{
    public string AccountId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string ProviderIcon { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;

    private string _statusIcon = "⏳";
    public string StatusIcon { get => _statusIcon; set => this.RaiseAndSetIfChanged(ref _statusIcon, value); }

    public ReactiveCommand<Unit, Unit> RemoveCommand { get; init; } = null!;
}

public class LoginViewModel : ReactiveObject
{
    private readonly AccountManager _accountManager;
    private readonly ITokenStore _tokenStore;
    private readonly ConfigService _configService;

    public ObservableCollection<AccountEntryViewModel> AccountEntries { get; } = new();

    private bool _hasAccounts;
    public bool HasAccounts { get => _hasAccounts; set => this.RaiseAndSetIfChanged(ref _hasAccounts, value); }

    // New account form fields
    private int _selectedProviderIndex;
    public int SelectedProviderIndex
    {
        get => _selectedProviderIndex;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedProviderIndex, value);
            this.RaisePropertyChanged(nameof(ShowServerUrl));
        }
    }

    public bool ShowServerUrl => SelectedProviderIndex > 0; // GitLab/Gitea need server URL

    private string _newDisplayName = string.Empty;
    public string NewDisplayName { get => _newDisplayName; set => this.RaiseAndSetIfChanged(ref _newDisplayName, value); }

    private string _newServerUrl = string.Empty;
    public string NewServerUrl { get => _newServerUrl; set => this.RaiseAndSetIfChanged(ref _newServerUrl, value); }

    private string _newUsername = string.Empty;
    public string NewUsername { get => _newUsername; set => this.RaiseAndSetIfChanged(ref _newUsername, value); }

    private string _newToken = string.Empty;
    public string NewToken { get => _newToken; set => this.RaiseAndSetIfChanged(ref _newToken, value); }

    private bool _newRememberToken;
    public bool NewRememberToken { get => _newRememberToken; set => this.RaiseAndSetIfChanged(ref _newRememberToken, value); }

    private string _errorMessage = string.Empty;
    public string ErrorMessage { get => _errorMessage; set => this.RaiseAndSetIfChanged(ref _errorMessage, value); }

    private bool _isConnecting;
    public bool IsConnecting { get => _isConnecting; set => this.RaiseAndSetIfChanged(ref _isConnecting, value); }

    public ReactiveCommand<Unit, Unit> AddAccountCommand { get; }
    public ReactiveCommand<Unit, string?> ConnectCommand { get; }

    public LoginViewModel(AccountManager accountManager, ITokenStore tokenStore, ConfigService configService)
    {
        _accountManager = accountManager;
        _tokenStore = tokenStore;
        _configService = configService;

        // Load existing configured accounts from config and try to restore tokens
        foreach (var account in configService.Config.Accounts)
        {
            var credKey = $"scmmom:{account.Id}";
            var saved = tokenStore.GetToken(credKey);
            if (saved != null)
            {
                account.Username = saved.Value.Username;
                AddAccountEntry(account, saved.Value.Token);
            }
            else
            {
                AddAccountEntry(account, null);
            }
        }
        HasAccounts = AccountEntries.Count > 0;

        var canAdd = this.WhenAnyValue(
            x => x.NewUsername, x => x.NewToken, x => x.IsConnecting,
            (u, t, c) => !string.IsNullOrWhiteSpace(u) && !string.IsNullOrWhiteSpace(t) && !c);

        AddAccountCommand = ReactiveCommand.CreateFromTask(AddAccountAsync, canAdd);

        var canConnect = this.WhenAnyValue(x => x.IsConnecting, x => x.HasAccounts,
            (c, h) => !c && h);
        ConnectCommand = ReactiveCommand.CreateFromTask(ConnectAllAsync, canConnect);
    }

    private void AddAccountEntry(ScmAccountConfig account, string? token)
    {
        var entry = new AccountEntryViewModel
        {
            AccountId = account.Id,
            DisplayName = account.DisplayName,
            ProviderIcon = account.ProviderType switch
            {
                ScmProviderType.GitHub => "🐙",
                ScmProviderType.GitLab => "🦊",
                ScmProviderType.Gitea => "🍵",
                _ => "🔗"
            },
            Detail = token != null ? $"{account.Username} — token saved" : $"{account.Username} — no token",
            StatusIcon = token != null ? "✅" : "⚠️",
            RemoveCommand = ReactiveCommand.Create(() => RemoveAccount(account.Id))
        };
        AccountEntries.Add(entry);
    }

    private void RemoveAccount(string accountId)
    {
        _configService.Config.Accounts.RemoveAll(a => a.Id == accountId);
        _configService.Save();
        _tokenStore.RemoveToken($"scmmom:{accountId}");
        _accountManager.RemoveProvider(accountId);

        var entry = AccountEntries.FirstOrDefault(e => e.AccountId == accountId);
        if (entry != null) AccountEntries.Remove(entry);
        HasAccounts = AccountEntries.Count > 0;
    }

    private async Task AddAccountAsync()
    {
        IsConnecting = true;
        ErrorMessage = string.Empty;

        try
        {
            var providerType = SelectedProviderIndex switch
            {
                1 => ScmProviderType.GitLab,
                2 => ScmProviderType.Gitea,
                _ => ScmProviderType.GitHub
            };

            var account = new ScmAccountConfig
            {
                ProviderType = providerType,
                DisplayName = string.IsNullOrWhiteSpace(NewDisplayName) ? $"{providerType}" : NewDisplayName.Trim(),
                ServerUrl = NewServerUrl.Trim(),
                Username = NewUsername.Trim(),
                RememberToken = NewRememberToken
            };

            // Validate by creating a provider and testing the token
            var provider = _accountManager.CreateProvider(account);
            provider.Initialize(NewToken.Trim(), NewUsername.Trim());
            var login = await provider.ValidateTokenAsync();

            _accountManager.AddProvider(provider);
            _configService.Config.Accounts.Add(account);
            _configService.Save();

            if (NewRememberToken)
            {
                _tokenStore.SaveToken($"scmmom:{account.Id}", NewUsername.Trim(), NewToken.Trim());
            }

            AddAccountEntry(account, NewToken.Trim());
            HasAccounts = true;

            // Clear form
            NewDisplayName = string.Empty;
            NewServerUrl = string.Empty;
            NewUsername = string.Empty;
            NewToken = string.Empty;
            NewRememberToken = false;
            SelectedProviderIndex = 0;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to add account: {ex.Message}";
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private async Task<string?> ConnectAllAsync()
    {
        IsConnecting = true;
        ErrorMessage = string.Empty;

        try
        {
            // For any accounts that aren't yet connected (no provider), try to connect with saved tokens
            foreach (var account in _configService.Config.Accounts)
            {
                if (_accountManager.GetProvider(account.Id) != null) continue;

                var credKey = $"scmmom:{account.Id}";
                var saved = _tokenStore.GetToken(credKey);
                if (saved == null)
                {
                    ErrorMessage = $"Account '{account.DisplayName}' has no saved token. Remove and re-add it.";
                    return null;
                }

                var provider = _accountManager.CreateProvider(account);
                provider.Initialize(saved.Value.Token, saved.Value.Username);
                await provider.ValidateTokenAsync();
                _accountManager.AddProvider(provider);
            }

            // Return the username of the first account
            var firstAccount = _configService.Config.Accounts.FirstOrDefault();
            return firstAccount?.Username ?? "User";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Connection failed: {ex.Message}";
            return null;
        }
        finally
        {
            IsConnecting = false;
        }
    }
}
