using System.Text.Json;
using Xunit;

namespace ClaudeUsage.Tests;

public sealed class UsageParserTests
{
    public UsageParserTests()
    {
        Loc.ForcedLanguage = "fr";
    }

    private const string RealPayload = """
    {
        "five_hour": { "utilization": 56.0, "resets_at": "2026-07-22T19:20:00.226808+00:00", "limit_dollars": null },
        "seven_day": { "utilization": 6.0, "resets_at": "2026-07-28T12:00:00.226829+00:00" },
        "seven_day_opus": null,
        "tangelo": null,
        "extra_usage": { "is_enabled": false, "used_credits": 0.0 },
        "limits": [
            { "kind": "session", "group": "session", "percent": 56, "severity": "normal", "resets_at": "2026-07-22T19:20:00.226808+00:00", "scope": null, "is_active": true },
            { "kind": "weekly_all", "group": "weekly", "percent": 6, "severity": "normal", "resets_at": "2026-07-28T12:00:00.226829+00:00", "scope": null, "is_active": false },
            { "kind": "weekly_scoped", "group": "weekly", "percent": 11, "severity": "normal", "resets_at": "2026-07-28T12:00:00.227122+00:00", "scope": { "model": { "id": null, "display_name": "Fable" }, "surface": null }, "is_active": false }
        ],
        "spend": { "percent": 0 },
        "member_dashboard_available": false
    }
    """;

    [Fact]
    public void Parse_RealPayload_ReturnsThreeOrderedRows()
    {
        var snapshot = UsageParser.Parse(RealPayload);

        Assert.Equal(3, snapshot.Rows.Count);
        Assert.Equal(new[] { "session", "weekly_all", "weekly_scoped" }, snapshot.Rows.Select(r => r.Kind));
        Assert.Equal(new[] { "Session courante (5h)", "Hebdomadaire", "Fable" }, snapshot.Rows.Select(r => r.Label));
        Assert.Equal(new[] { 56.0, 6.0, 11.0 }, snapshot.Rows.Select(r => r.Percent));
        Assert.All(snapshot.Rows, r => Assert.Equal("normal", r.Severity));
        Assert.Equal(DateTimeOffset.Parse("2026-07-22T19:20:00.226808+00:00"), snapshot.Rows[0].ResetsAt);
        Assert.Equal(DateTimeOffset.Parse("2026-07-28T12:00:00.226829+00:00"), snapshot.Rows[1].ResetsAt);
    }

    [Fact]
    public void Parse_DuplicateKind_PrefersActiveEntry()
    {
        const string json = """
        {
            "limits": [
                { "kind": "session", "percent": 10, "is_active": false },
                { "kind": "session", "percent": 90, "is_active": true }
            ]
        }
        """;

        var snapshot = UsageParser.Parse(json);

        var row = Assert.Single(snapshot.Rows);
        Assert.Equal(90.0, row.Percent);
    }

    [Fact]
    public void Parse_DuplicateKindWithoutActive_KeepsFirstEntry()
    {
        const string json = """
        {
            "limits": [
                { "kind": "session", "percent": 10, "is_active": false },
                { "kind": "session", "percent": 90, "is_active": false }
            ]
        }
        """;

        var snapshot = UsageParser.Parse(json);

        var row = Assert.Single(snapshot.Rows);
        Assert.Equal(10.0, row.Percent);
    }

    [Fact]
    public void Parse_ScopedWithoutDisplayName_UsesDefaultLabel()
    {
        const string json = """
        {
            "limits": [
                { "kind": "weekly_scoped", "percent": 11, "scope": { "model": { "id": null } } }
            ]
        }
        """;

        var snapshot = UsageParser.Parse(json);

        var row = Assert.Single(snapshot.Rows);
        Assert.Equal("Modèle", row.Label);
    }

    [Fact]
    public void Parse_MissingLimits_FallsBackToLegacyBlocks()
    {
        const string json = """
        {
            "five_hour": { "utilization": 56.5, "resets_at": "2026-07-22T19:20:00+00:00" },
            "seven_day": { "utilization": 6.0, "resets_at": "2026-07-28T12:00:00+00:00" }
        }
        """;

        var snapshot = UsageParser.Parse(json);

        Assert.Equal(2, snapshot.Rows.Count);
        Assert.Equal(new[] { "session", "weekly_all" }, snapshot.Rows.Select(r => r.Kind));
        Assert.Equal(new[] { "Session courante (5h)", "Hebdomadaire" }, snapshot.Rows.Select(r => r.Label));
        Assert.Equal(56.5, snapshot.Rows[0].Percent);
        Assert.All(snapshot.Rows, r => Assert.Null(r.Severity));
    }

    [Fact]
    public void Parse_EmptyLimitsArray_FallsBackToLegacyBlocks()
    {
        const string json = """
        {
            "limits": [],
            "five_hour": { "utilization": 12.0, "resets_at": "2026-07-22T19:20:00+00:00" }
        }
        """;

        var snapshot = UsageParser.Parse(json);

        var row = Assert.Single(snapshot.Rows);
        Assert.Equal("session", row.Kind);
        Assert.Equal(12.0, row.Percent);
    }

    [Fact]
    public void Parse_UnknownKindsOnlyAndNoLegacy_ReturnsEmptySnapshot()
    {
        const string json = """
        {
            "limits": [
                { "kind": "daily_mystery", "percent": 42 }
            ]
        }
        """;

        var snapshot = UsageParser.Parse(json);

        Assert.Empty(snapshot.Rows);
    }

    [Fact]
    public void Parse_EntryWithoutPercent_IsSkipped()
    {
        const string json = """
        {
            "limits": [
                { "kind": "session", "severity": "normal" },
                { "kind": "weekly_all", "percent": 6 }
            ]
        }
        """;

        var snapshot = UsageParser.Parse(json);

        var row = Assert.Single(snapshot.Rows);
        Assert.Equal("weekly_all", row.Kind);
    }

    [Fact]
    public void Parse_InvalidResetsAt_YieldsRowWithoutReset()
    {
        const string json = """
        {
            "limits": [
                { "kind": "session", "percent": 56, "resets_at": "pas une date" }
            ]
        }
        """;

        var snapshot = UsageParser.Parse(json);

        var row = Assert.Single(snapshot.Rows);
        Assert.Null(row.ResetsAt);
    }

    [Fact]
    public void Parse_MalformedJson_ThrowsJsonException()
    {
        Assert.ThrowsAny<JsonException>(() => UsageParser.Parse("{ pas du json"));
    }

    [Fact]
    public void Parse_EnglishLanguage_UsesEnglishLabels()
    {
        Loc.ForcedLanguage = "en";

        var snapshot = UsageParser.Parse(RealPayload);

        Assert.Equal(new[] { "Current session (5h)", "Weekly", "Fable" }, snapshot.Rows.Select(r => r.Label));
    }

    [Fact]
    public void Parse_UnknownLanguage_FallsBackToEnglishLabels()
    {
        Loc.ForcedLanguage = "tlh";

        var snapshot = UsageParser.Parse(RealPayload);

        Assert.Equal(new[] { "Current session (5h)", "Weekly", "Fable" }, snapshot.Rows.Select(r => r.Label));
    }
}
