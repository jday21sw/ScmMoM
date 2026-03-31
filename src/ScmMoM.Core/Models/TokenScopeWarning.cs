namespace ScmMoM.Core.Models;

public class TokenScopeWarning
{
    public IReadOnlyList<string> ExcessiveScopes { get; init; } = [];
    public IReadOnlyList<string> RecommendedScopes { get; init; } = [];
    public string Message { get; init; } = string.Empty;
}
