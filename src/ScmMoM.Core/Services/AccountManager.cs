using ScmMoM.Core.Models;

namespace ScmMoM.Core.Services;

public class AccountManager
{
    private readonly Dictionary<string, IScmProvider> _providers = new();

    public IReadOnlyDictionary<string, IScmProvider> Providers => _providers;

    public IScmProvider? ActiveProvider => _providers.Values.FirstOrDefault();

    public IScmProvider CreateProvider(ScmAccountConfig account)
    {
        return account.ProviderType switch
        {
            ScmProviderType.GitHub => new GitHubProvider(account),
            _ => throw new NotSupportedException($"Provider type {account.ProviderType} is not yet supported.")
        };
    }

    public void AddProvider(IScmProvider provider)
    {
        _providers[provider.AccountId] = provider;
    }

    public IScmProvider? GetProvider(string accountId)
    {
        return _providers.GetValueOrDefault(accountId);
    }

    public void RemoveProvider(string accountId)
    {
        _providers.Remove(accountId);
    }
}
