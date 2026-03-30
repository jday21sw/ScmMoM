using System.Reactive;
using System.Reactive.Linq;
using ScmMoM.Core.Models;
using ScmMoM.Core.Services;
using ReactiveUI;

namespace ScmMoM.UI.ViewModels;

public class LoginViewModel : ReactiveObject
{
    private readonly AccountManager _accountManager;
    private readonly ITokenStore _tokenStore;
    private readonly ConfigService _configService;
    private const string CredentialKey = "github";

    private string _username = string.Empty;
    public string Username
    {
        get => _username;
        set => this.RaiseAndSetIfChanged(ref _username, value);
    }

    private string _token = string.Empty;
    public string Token
    {
        get => _token;
        set => this.RaiseAndSetIfChanged(ref _token, value);
    }

    private string _errorMessage = string.Empty;
    public string ErrorMessage
    {
        get => _errorMessage;
        set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    private bool _isConnecting;
    public bool IsConnecting
    {
        get => _isConnecting;
        set => this.RaiseAndSetIfChanged(ref _isConnecting, value);
    }

    private bool _rememberToken;
    public bool RememberToken
    {
        get => _rememberToken;
        set => this.RaiseAndSetIfChanged(ref _rememberToken, value);
    }

    private bool _hasSavedToken;
    public bool HasSavedToken
    {
        get => _hasSavedToken;
        set => this.RaiseAndSetIfChanged(ref _hasSavedToken, value);
    }

    public ReactiveCommand<Unit, string?> ConnectCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearSavedTokenCommand { get; }

    public LoginViewModel(AccountManager accountManager, ITokenStore tokenStore, ConfigService configService)
    {
        _accountManager = accountManager;
        _tokenStore = tokenStore;
        _configService = configService;

        // Try to load saved credentials
        var saved = _tokenStore.GetToken(CredentialKey);
        if (saved != null)
        {
            Username = saved.Value.Username;
            Token = saved.Value.Token;
            RememberToken = true;
            HasSavedToken = true;
        }

        var canConnect = this.WhenAnyValue(
            x => x.Username, x => x.Token, x => x.IsConnecting,
            (u, t, c) => !string.IsNullOrWhiteSpace(u) && !string.IsNullOrWhiteSpace(t) && !c);

        ConnectCommand = ReactiveCommand.CreateFromTask(ConnectAsync, canConnect);

        ClearSavedTokenCommand = ReactiveCommand.Create(() =>
        {
            _tokenStore.RemoveToken(CredentialKey);
            Token = string.Empty;
            HasSavedToken = false;
            RememberToken = false;
        });
    }

    private async Task<string?> ConnectAsync()
    {
        IsConnecting = true;
        ErrorMessage = string.Empty;

        try
        {
            // Get or create the default account config
            var account = _configService.Config.Accounts.FirstOrDefault();
            if (account == null)
            {
                account = new ScmAccountConfig
                {
                    Id = "default",
                    ProviderType = ScmProviderType.GitHub,
                    DisplayName = "GitHub"
                };
                _configService.Config.Accounts.Add(account);
                _configService.Save();
            }

            var provider = _accountManager.CreateProvider(account);
            provider.Initialize(Token.Trim(), Username.Trim());
            var login = await provider.ValidateTokenAsync();

            _accountManager.AddProvider(provider);

            // Save or clear token based on checkbox
            if (RememberToken)
            {
                _tokenStore.SaveToken(CredentialKey, Username.Trim(), Token.Trim());
            }
            else
            {
                _tokenStore.RemoveToken(CredentialKey);
            }

            return login;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Authentication failed: {ex.Message}";
            return null;
        }
        finally
        {
            IsConnecting = false;
        }
    }
}
