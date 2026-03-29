using System.Text.Json;
using BPM.Core.Models;

namespace BPM.Core.Services;

public class ConfigService
{
    private readonly string _configPath;

    public AppConfig Config { get; private set; } = new();

    public ConfigService(string configPath)
    {
        _configPath = configPath;
        Load();
    }

    public void Load()
    {
        if (File.Exists(_configPath))
        {
            var json = File.ReadAllText(_configPath);
            Config = JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new AppConfig();
        }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(Config, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(_configPath, json);
    }
}
