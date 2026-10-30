using WeeklyReport.Application;

namespace WeeklyReport.UnitTests;

public class ReminderActivationTests
{
    [Theory]
    [InlineData("action=weekly-report;week=2026-10-26")]
    [InlineData("action=weekly-report&week=2026-10-26")]
    public void TryParse_AcceptsOnlyTheExpectedWeekAndAction(string arguments)
    {
        Assert.True(ReminderActivation.TryParse(arguments, out var week));
        Assert.Equal(new DateOnly(2026, 10, 26), week);
    }

    [Theory]
    [InlineData("")]
    [InlineData("action=other&week=2026-10-26")]
    [InlineData("action=weekly-report&week=2026-10-30")]
    [InlineData("action=weekly-report&week=2026-10-26&week=2026-11-02")]
    [InlineData("action=weekly-report&week=2026-10-26&extra=value")]
    [InlineData("action=weekly-report&week=not-a-date")]
    [InlineData("action=weekly-report;week=2026-10-26;week=2026-11-02")]
    [InlineData("action=weekly-report;week=2026-10-26&extra=value")]
    [InlineData("action=weekly-report;;week=2026-10-26")]
    public void TryParse_RejectsMalformedOrUnexpectedArguments(string arguments)
    {
        Assert.False(ReminderActivation.TryParse(arguments, out _));
    }

    [Fact]
    public void TryParseValues_ValidatesTheSdkArgumentMap()
    {
        var values = new Dictionary<string, string>
        {
            ["action"] = "weekly-report",
            ["week"] = "2026-10-26"
        };
        Assert.True(ReminderActivation.TryParseValues(values, out var week));
        Assert.Equal(new DateOnly(2026, 10, 26), week);
        values["extra"] = "ignored";
        Assert.False(ReminderActivation.TryParseValues(values, out _));
    }
}
