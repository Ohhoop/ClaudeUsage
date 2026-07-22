using Xunit;

namespace ClaudeUsage.Tests;

public sealed class CountdownFormatterTests
{
    public CountdownFormatterTests()
    {
        Loc.ForcedLanguage = "fr";
    }

    [Theory]
    [InlineData(0, 0, 0, 30, "1 min")]
    [InlineData(0, 0, 33, 10, "34 min")]
    [InlineData(0, 0, 34, 0, "34 min")]
    [InlineData(0, 0, 59, 0, "59 min")]
    [InlineData(0, 0, 59, 30, "1 h 00")]
    [InlineData(0, 1, 0, 0, "1 h 00")]
    [InlineData(0, 2, 5, 0, "2 h 05")]
    [InlineData(0, 2, 34, 0, "2 h 34")]
    [InlineData(0, 23, 59, 0, "23 h 59")]
    [InlineData(0, 23, 59, 59, "23 h 59")]
    [InlineData(1, 0, 0, 0, "1 j 0 h")]
    [InlineData(5, 17, 0, 0, "5 j 17 h")]
    [InlineData(5, 17, 45, 0, "5 j 17 h")]
    [InlineData(0, 0, 0, 0, "0 min")]
    [InlineData(0, 0, -5, 0, "0 min")]
    public void Format_ReturnsExpectedText(int days, int hours, int minutes, int seconds, string expected)
    {
        Assert.Equal(expected, CountdownFormatter.Format(new TimeSpan(days, hours, minutes, seconds)));
    }

    [Fact]
    public void Format_EnglishLanguage_UsesEnglishDayUnit()
    {
        Loc.ForcedLanguage = "en";

        Assert.Equal("5 d 17 h", CountdownFormatter.Format(new TimeSpan(5, 17, 0, 0)));
    }

    [Fact]
    public void Format_WithExplicitFormats_UsesGivenPatterns()
    {
        var formats = new CountdownFormats("0m", "{0}m", "{0}h{1:00}", "{0}d {1}h");

        Assert.Equal("0m", CountdownFormatter.Format(TimeSpan.Zero, formats));
        Assert.Equal("34m", CountdownFormatter.Format(new TimeSpan(0, 34, 0), formats));
        Assert.Equal("2h05", CountdownFormatter.Format(new TimeSpan(2, 5, 0), formats));
        Assert.Equal("5d 17h", CountdownFormatter.Format(new TimeSpan(5, 17, 0, 0), formats));
    }
}
