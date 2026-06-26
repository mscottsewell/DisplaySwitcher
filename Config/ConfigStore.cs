using System.Text.Json;
using DisplaySwitcher.Model;

namespace DisplaySwitcher.Config;

/// <summary>
/// Loads and saves the preset list as JSON in %AppData%\DisplaySwitcher\presets.json.
/// </summary>
public static class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static string ConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DisplaySwitcher");

    public static string ConfigPath => Path.Combine(ConfigDirectory, "presets.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                string json = File.ReadAllText(ConfigPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                if (config != null)
                    return config;
            }
        }
        catch
        {
            // Fall through and return an empty config rather than crashing the tray app.
        }

        return new AppConfig();
    }

    public static void Save(AppConfig config)
    {
        Directory.CreateDirectory(ConfigDirectory);
        string json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }
}
