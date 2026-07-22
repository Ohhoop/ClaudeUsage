using System.Globalization;
using System.Text.Json;

namespace ClaudeUsage;

public static class UsageParser
{
    public static UsageSnapshot Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var rows = ParseLimits(root);
        if (rows.Count == 0)
        {
            rows = ParseLegacy(root);
        }

        return new UsageSnapshot(rows);
    }

    private static List<LimitRow> ParseLimits(JsonElement root)
    {
        var rows = new List<LimitRow>();
        if (!root.TryGetProperty("limits", out var limits) || limits.ValueKind != JsonValueKind.Array)
        {
            return rows;
        }

        var byKind = new Dictionary<string, JsonElement>();
        foreach (var entry in limits.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var kind = GetString(entry, "kind");
            if (kind is null)
            {
                continue;
            }

            if (!byKind.TryGetValue(kind, out var existing) || (!IsActive(existing) && IsActive(entry)))
            {
                byKind[kind] = entry;
            }
        }

        AddRow(rows, byKind, "session", _ => "Session courante (5h)");
        AddRow(rows, byKind, "weekly_all", _ => "Hebdomadaire");
        AddRow(rows, byKind, "weekly_scoped", ScopedLabel);
        return rows;
    }

    private static void AddRow(List<LimitRow> rows, Dictionary<string, JsonElement> byKind, string kind, Func<JsonElement, string> label)
    {
        if (!byKind.TryGetValue(kind, out var entry))
        {
            return;
        }

        if (!entry.TryGetProperty("percent", out var percent) || percent.ValueKind != JsonValueKind.Number)
        {
            return;
        }

        rows.Add(new LimitRow(kind, label(entry), percent.GetDouble(), GetString(entry, "severity"), GetResetsAt(entry)));
    }

    private static string ScopedLabel(JsonElement entry)
    {
        if (entry.TryGetProperty("scope", out var scope) && scope.ValueKind == JsonValueKind.Object
            && scope.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.Object)
        {
            var name = GetString(model, "display_name");
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        return "Modèle";
    }

    private static List<LimitRow> ParseLegacy(JsonElement root)
    {
        var rows = new List<LimitRow>();
        AddLegacyRow(rows, root, "five_hour", "session", "Session courante (5h)");
        AddLegacyRow(rows, root, "seven_day", "weekly_all", "Hebdomadaire");
        return rows;
    }

    private static void AddLegacyRow(List<LimitRow> rows, JsonElement root, string property, string kind, string label)
    {
        if (!root.TryGetProperty(property, out var entry) || entry.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (!entry.TryGetProperty("utilization", out var utilization) || utilization.ValueKind != JsonValueKind.Number)
        {
            return;
        }

        rows.Add(new LimitRow(kind, label, utilization.GetDouble(), null, GetResetsAt(entry)));
    }

    private static bool IsActive(JsonElement entry)
        => entry.TryGetProperty("is_active", out var active) && active.ValueKind == JsonValueKind.True;

    private static string? GetString(JsonElement entry, string name)
        => entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateTimeOffset? GetResetsAt(JsonElement entry)
    {
        if (!entry.TryGetProperty("resets_at", out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        if (value.TryGetDateTimeOffset(out var parsed))
        {
            return parsed;
        }

        return DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var fallback)
            ? fallback
            : null;
    }
}
