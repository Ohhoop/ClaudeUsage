using System.Text.Json;

namespace ClaudeUsage;

public sealed class AppSettings
{
    public int? X { get; set; }

    public int? Y { get; set; }

    public int OpacityPercent { get; set; } = 85;

    public bool NotifyOnReset { get; set; } = true;

    public Dictionary<string, int> ApproachThresholds { get; set; } = new();

    public DateTimeOffset? RateLimitUntil { get; set; }
}

public static class SettingsStore
{
    public static readonly int[] OpacityLevels = { 55, 70, 85, 100 };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private static string Folder
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeUsage");

    private static string FilePath => Path.Combine(Folder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
            if (settings is null)
            {
                return new AppSettings();
            }

            if (!OpacityLevels.Contains(settings.OpacityPercent))
            {
                settings.OpacityPercent = 85;
            }

            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var temporaryPath = FilePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, WriteOptions));
            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        catch
        {
        }
    }
}
