using System.Globalization;
using System.Text.Json;

namespace ClaudeUsage;

public sealed record CountdownFormats(string Zero, string Minutes, string HoursMinutes, string DaysHours);

public static class Loc
{
    private static readonly object Sync = new();
    private static Dictionary<string, Dictionary<string, string>>? _tables;
    private static List<Dictionary<string, string>>? _chain;
    private static string? _forcedLanguage;

    public static string? ForcedLanguage
    {
        get => _forcedLanguage;
        set
        {
            lock (Sync)
            {
                _forcedLanguage = value;
                _chain = null;
            }
        }
    }

    public static string T(string key)
    {
        foreach (var table in Chain())
        {
            if (table.TryGetValue(key, out var value))
            {
                return value;
            }
        }

        return key;
    }

    public static CountdownFormats GetCountdownFormats()
        => new(T("countdown.zero"), T("countdown.minutes"), T("countdown.hoursMinutes"), T("countdown.daysHours"));

    private static List<Dictionary<string, string>> Chain()
    {
        lock (Sync)
        {
            if (_chain is not null)
            {
                return _chain;
            }

            _tables ??= Load();
            var chain = new List<Dictionary<string, string>>();
            foreach (var name in CandidateNames())
            {
                if (_tables.TryGetValue(name, out var table) && !chain.Contains(table))
                {
                    chain.Add(table);
                }
            }

            if (_tables.TryGetValue("en", out var english) && !chain.Contains(english))
            {
                chain.Add(english);
            }

            _chain = chain;
            return chain;
        }
    }

    private static IEnumerable<string> CandidateNames()
    {
        if (!string.IsNullOrEmpty(_forcedLanguage))
        {
            yield return _forcedLanguage;
            yield break;
        }

        var culture = CultureInfo.CurrentUICulture;
        while (!string.IsNullOrEmpty(culture.Name))
        {
            yield return culture.Name;
            culture = culture.Parent;
        }
    }

    private static Dictionary<string, Dictionary<string, string>> Load()
    {
        var tables = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        using (var stream = typeof(Loc).Assembly.GetManifestResourceStream("ClaudeUsage.Assets.translations.json"))
        {
            if (stream is not null)
            {
                Merge(tables, stream);
            }
        }

        try
        {
            var externalPath = Path.Combine(AppContext.BaseDirectory, "translations.json");
            if (File.Exists(externalPath))
            {
                using var external = File.OpenRead(externalPath);
                Merge(tables, external);
            }
        }
        catch
        {
        }

        return tables;
    }

    private static void Merge(Dictionary<string, Dictionary<string, string>> tables, Stream stream)
    {
        try
        {
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (var language in document.RootElement.EnumerateObject())
            {
                if (language.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!tables.TryGetValue(language.Name, out var table))
                {
                    table = new Dictionary<string, string>(StringComparer.Ordinal);
                    tables[language.Name] = table;
                }

                foreach (var entry in language.Value.EnumerateObject())
                {
                    if (entry.Value.ValueKind == JsonValueKind.String)
                    {
                        table[entry.Name] = entry.Value.GetString()!;
                    }
                }
            }
        }
        catch
        {
        }
    }
}
