namespace ClaudeUsage;

public static class CountdownFormatter
{
    public static string Format(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
        {
            return "0 min";
        }

        if (remaining < TimeSpan.FromHours(1))
        {
            var minutes = (int)Math.Ceiling(remaining.TotalMinutes);
            return minutes >= 60 ? "1 h 00" : $"{minutes} min";
        }

        if (remaining < TimeSpan.FromDays(1))
        {
            return $"{(int)remaining.TotalHours} h {remaining.Minutes:00}";
        }

        return $"{remaining.Days} j {remaining.Hours} h";
    }
}
