namespace ScmMoM.Core.Services;

public interface ITokenStore
{
    void SaveToken(string serviceKey, string username, string token);
    (string Username, string Token)? GetToken(string serviceKey);
    void RemoveToken(string serviceKey);
}
