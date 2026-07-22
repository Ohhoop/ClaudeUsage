namespace ClaudeUsage;

public static class SeverityColors
{
    public static readonly Color Neutral = Color.FromArgb(96, 165, 250);
    public static readonly Color Warning = Color.FromArgb(251, 146, 60);
    public static readonly Color Critical = Color.FromArgb(248, 113, 113);
    public static readonly Color Track = Color.FromArgb(58, 58, 62);
    public static readonly Color Text = Color.FromArgb(222, 222, 224);
    public static readonly Color MutedText = Color.FromArgb(150, 150, 154);

    public static Color ForLimit(string? severity, double percent)
    {
        var byThreshold = percent >= 95 ? Critical : percent >= 80 ? Warning : Neutral;
        return severity?.ToLowerInvariant() switch
        {
            "warning" => byThreshold == Critical ? Critical : Warning,
            "exceeded" or "critical" or "rejected" or "blocked" => Critical,
            _ => byThreshold,
        };
    }
}
