using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeUsage;

public static class SessionHook
{
    private const string EventName = "SessionStart";
    private const string ExecutableMarker = "ClaudeUsage.exe";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude",
        "settings.json");

    public static bool Exists() => FindCommand() is not null;

    public static bool MatchesCurrentPath() => FindCommand() == ExpectedCommand();

    public static bool TryEnable()
    {
        if (ExpectedCommand() is not string command)
        {
            return false;
        }

        try
        {
            var root = LoadRoot();
            if (root is null)
            {
                return false;
            }

            if (root["hooks"] is not JsonObject hooks)
            {
                hooks = new JsonObject();
                root["hooks"] = hooks;
            }

            if (hooks[EventName] is not JsonArray sessionStart)
            {
                sessionStart = new JsonArray();
                hooks[EventName] = sessionStart;
            }

            RemoveOwnEntries(sessionStart);
            sessionStart.Add(new JsonObject
            {
                ["hooks"] = new JsonArray(new JsonObject
                {
                    ["type"] = "command",
                    ["shell"] = "powershell",
                    ["command"] = command,
                    ["timeout"] = 15,
                    ["async"] = true,
                }),
            });

            Save(root);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryDisable()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return true;
            }

            var root = LoadRoot();
            if (root is null)
            {
                return false;
            }

            if (root["hooks"] is not JsonObject hooks || hooks[EventName] is not JsonArray sessionStart)
            {
                return true;
            }

            RemoveOwnEntries(sessionStart);
            if (sessionStart.Count == 0)
            {
                hooks.Remove(EventName);
            }

            if (hooks.Count == 0)
            {
                root.Remove("hooks");
            }

            Save(root);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? ExpectedCommand()
    {
        var executablePath = Environment.ProcessPath;
        return string.IsNullOrEmpty(executablePath) ? null : $"Start-Process -FilePath '{executablePath}'";
    }

    private static string? FindCommand()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return null;
            }

            var root = LoadRoot();
            if (root?["hooks"] is not JsonObject hooks || hooks[EventName] is not JsonArray sessionStart)
            {
                return null;
            }

            foreach (var group in sessionStart)
            {
                if (group is not JsonObject groupObject || groupObject["hooks"] is not JsonArray entries)
                {
                    continue;
                }

                foreach (var entry in entries)
                {
                    if (entry is JsonObject entryObject && IsOwnEntry(entryObject))
                    {
                        return entryObject["command"]?.GetValue<string>();
                    }
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static void RemoveOwnEntries(JsonArray sessionStart)
    {
        for (var groupIndex = sessionStart.Count - 1; groupIndex >= 0; groupIndex--)
        {
            if (sessionStart[groupIndex] is not JsonObject groupObject || groupObject["hooks"] is not JsonArray entries)
            {
                continue;
            }

            for (var entryIndex = entries.Count - 1; entryIndex >= 0; entryIndex--)
            {
                if (entries[entryIndex] is JsonObject entryObject && IsOwnEntry(entryObject))
                {
                    entries.RemoveAt(entryIndex);
                }
            }

            if (entries.Count == 0)
            {
                sessionStart.RemoveAt(groupIndex);
            }
        }
    }

    private static bool IsOwnEntry(JsonObject entry)
    {
        if (entry["command"] is not JsonValue value || !value.TryGetValue<string>(out var command))
        {
            return false;
        }

        return command.Contains(ExecutableMarker, StringComparison.OrdinalIgnoreCase);
    }

    private static JsonObject? LoadRoot()
    {
        if (!File.Exists(SettingsPath))
        {
            return new JsonObject();
        }

        using var stream = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return JsonNode.Parse(stream) as JsonObject;
    }

    private static void Save(JsonObject root)
    {
        var temporaryPath = SettingsPath + ".tmp";
        File.WriteAllText(temporaryPath, root.ToJsonString(WriteOptions));
        File.Move(temporaryPath, SettingsPath, overwrite: true);
    }
}
