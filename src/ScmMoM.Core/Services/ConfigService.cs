using System.Text.Json;
using ScmMoM.Core.Models;

namespace ScmMoM.Core.Services;

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

        MigrateIfNeeded();
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(Config, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(_configPath, json);
    }

    private void MigrateIfNeeded()
    {
        // Migrate from old flat format (Organization + Repositories) to Accounts list
        if (Config.Accounts.Count == 0 && !string.IsNullOrWhiteSpace(Config.Organization))
        {
            Config.Accounts.Add(new ScmAccountConfig
            {
                Id = "default",
                ProviderType = ScmProviderType.GitHub,
                DisplayName = Config.Organization,
                Organization = Config.Organization,
                Repositories = Config.Repositories ?? new()
            });

            // Clear legacy fields
            Config.Organization = null;
            Config.Repositories = null;
            Save();
        }
    }
}
