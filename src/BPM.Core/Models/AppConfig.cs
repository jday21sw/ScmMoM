namespace BPM.Core.Models;

public class AppConfig
{
    public string Organization { get; set; } = "21sw-us";
    public List<string> Repositories { get; set; } = new()
    {
        "tsel",
        "meta-21sw",
        "meta-21sw-extras",
        "LAVA-docker-compose",
        "TSEL-GitHub-runner"
    };
    public int RefreshIntervalSeconds { get; set; } = 300;
    public bool NotificationsEnabled { get; set; } = true;
    public string ThemeMode { get; set; } = "System";
    public bool WebServerEnabled { get; set; } = false;
    public int WebServerPort { get; set; } = 5123;
}
