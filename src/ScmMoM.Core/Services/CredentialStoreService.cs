using GCM = GitCredentialManager;

namespace ScmMoM.Core.Services;

public class CredentialStoreService : ITokenStore
{
    private const string Namespace = "ScmMoM";
    private const string ServicePrefix = "scmmom://accounts/";
    private readonly GCM.ICredentialStore _store;

    public CredentialStoreService()
    {
        _store = GCM.CredentialManager.Create(Namespace);
    }

    public void SaveToken(string serviceKey, string username, string token)
    {
        _store.AddOrUpdate(ServicePrefix + serviceKey, username, token);
    }

    public (string Username, string Token)? GetToken(string serviceKey)
    {
        try
        {
            // Try new key format first, fall back to legacy format
            var credential = _store.Get(ServicePrefix + serviceKey, null);
            if (credential == null)
                credential = _store.Get("scmmom://" + serviceKey, null);
            if (credential != null)
            {
                return (credential.Account, credential.Password);
            }
        }
        catch
        {
            // Credential not found or store unavailable
        }
        return null;
    }

    public void RemoveToken(string serviceKey)
    {
        try
        {
            _store.Remove(ServicePrefix + serviceKey, null);
        }
        catch
        {
            // Credential not found or store unavailable
        }
        try
        {
            _store.Remove("scmmom://" + serviceKey, null);
        }
        catch
        {
            // Legacy key not found
        }
    }
}
