using System.Globalization;

namespace ClaudeUsage;

public static class CountdownFormatter
{
    public static string Format(TimeSpan remaining) => Format(remaining, Loc.GetCountdownFormats());

    public static string Format(TimeSpan remaining, CountdownFormats formats)
    {
        if (remaining <= TimeSpan.Zero)
        {
            return formats.Zero;
        }

        if (remaining < TimeSpan.FromHours(1))
        {
            var minutes = (int)Math.Ceiling(remaining.TotalMinutes);
            return minutes >= 60
                ? string.Format(CultureInfo.InvariantCulture, formats.HoursMinutes, 1, 0)
                : string.Format(CultureInfo.InvariantCulture, formats.Minutes, minutes);
        }

        if (remaining < TimeSpan.FromDays(1))
        {
            return string.Format(CultureInfo.InvariantCulture, formats.HoursMinutes, (int)remaining.TotalHours, remaining.Minutes);
        }

        return string.Format(CultureInfo.InvariantCulture, formats.DaysHours, remaining.Days, remaining.Hours);
    }
}
