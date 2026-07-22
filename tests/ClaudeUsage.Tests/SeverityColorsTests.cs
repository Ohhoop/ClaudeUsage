using Xunit;

namespace ClaudeUsage.Tests;

public sealed class SeverityColorsTests
{
    [Theory]
    [InlineData("normal", 56.0)]
    [InlineData("NORMAL", 56.0)]
    [InlineData(null, 79.0)]
    [InlineData("mystere", 10.0)]
    public void ForLimit_LowUsage_ReturnsNeutral(string? severity, double percent)
    {
        Assert.Equal(SeverityColors.Neutral, SeverityColors.ForLimit(severity, percent));
    }

    [Theory]
    [InlineData("normal", 80.0)]
    [InlineData(null, 80.0)]
    [InlineData(null, 94.0)]
    [InlineData("warning", 10.0)]
    [InlineData("WARNING", 10.0)]
    public void ForLimit_WarningZone_ReturnsWarning(string? severity, double percent)
    {
        Assert.Equal(SeverityColors.Warning, SeverityColors.ForLimit(severity, percent));
    }

    [Theory]
    [InlineData("normal", 95.0)]
    [InlineData(null, 95.0)]
    [InlineData("warning", 96.0)]
    [InlineData("exceeded", 1.0)]
    [InlineData("critical", 1.0)]
    [InlineData("rejected", 1.0)]
    [InlineData("blocked", 1.0)]
    public void ForLimit_CriticalZone_ReturnsCritical(string? severity, double percent)
    {
        Assert.Equal(SeverityColors.Critical, SeverityColors.ForLimit(severity, percent));
    }
}
