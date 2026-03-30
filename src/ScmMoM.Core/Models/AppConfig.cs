using System.Text.Json.Serialization;

namespace ScmMoM.Core.Models;

public class AppConfig
{
    // Multi-account support
    public List<ScmAccountConfig> Accounts { get; set; } = new();

    // Global settings
    public int RefreshIntervalSeconds { get; set; } = 300;
    public bool NotificationsEnabled { get; set; } = true;
    public string ThemeMode { get; set; } = "System";
    public bool WebServerEnabled { get; set; } = false;
    public int WebServerPort { get; set; } = 5123;

    // Legacy fields — used only for migration from old flat config format
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Organization { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Repositories { get; set; }
}
