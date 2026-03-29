using System.Reactive;
using System.Reactive.Linq;
using BPM.Core.Services;
using ReactiveUI;

namespace BPM.UI.ViewModels;

public class LoginViewModel : ReactiveObject
{
    private readonly IGitHubService _gitHubService;

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

    public ReactiveCommand<Unit, string?> ConnectCommand { get; }

    public LoginViewModel(IGitHubService gitHubService)
    {
        _gitHubService = gitHubService;

        var canConnect = this.WhenAnyValue(
            x => x.Username, x => x.Token, x => x.IsConnecting,
            (u, t, c) => !string.IsNullOrWhiteSpace(u) && !string.IsNullOrWhiteSpace(t) && !c);

        ConnectCommand = ReactiveCommand.CreateFromTask(ConnectAsync, canConnect);
    }

    private async Task<string?> ConnectAsync()
    {
        IsConnecting = true;
        ErrorMessage = string.Empty;

        try
        {
            _gitHubService.Initialize(Token.Trim(), Username.Trim());
            var login = await _gitHubService.ValidateTokenAsync();
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
