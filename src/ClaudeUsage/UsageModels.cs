namespace ClaudeUsage;

public sealed record LimitRow(string Kind, string Label, double Percent, string? Severity, DateTimeOffset? ResetsAt);

public sealed record UsageSnapshot(IReadOnlyList<LimitRow> Rows);
