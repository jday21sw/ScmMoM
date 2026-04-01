namespace ScmMoM.Core.Models;

public enum ScmProviderType
{
    GitHub,
    GitLab,
    Gitea
}

public class ScmAccountConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public ScmProviderType ProviderType { get; set; } = ScmProviderType.GitHub;
    public string DisplayName { get; set; } = string.Empty;
    public string ServerUrl { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Organization { get; set; } = string.Empty;
    public List<string> Repositories { get; set; } = new();
    public bool RememberToken { get; set; }
}
