using WeeklyReport.Domain;

namespace WeeklyReport.UnitTests;

public class WeekPeriodTests
{
    [Theory]
    [InlineData(2026, 10, 26)]
    [InlineData(2026, 10, 27)]
    [InlineData(2026, 10, 30)]
    [InlineData(2026, 11, 1)]
    public void FromDate_ReturnsMondayThroughSunday(int year, int month, int day)
    {
        var week = WeekPeriod.FromDate(new DateOnly(year, month, day));

        Assert.Equal(new DateOnly(2026, 10, 26), week.Start);
        Assert.Equal(new DateOnly(2026, 11, 1), week.EndInclusive);
        Assert.Equal(new DateOnly(2026, 11, 2), week.EndExclusive);
    }

    [Fact]
    public void Contains_UsesAnInclusiveStartAndExclusiveEnd()
    {
        var week = WeekPeriod.FromDate(new DateOnly(2026, 12, 31));

        Assert.True(week.Contains(new DateOnly(2026, 12, 28)));
        Assert.True(week.Contains(new DateOnly(2027, 1, 3)));
        Assert.False(week.Contains(new DateOnly(2026, 12, 27)));
        Assert.False(week.Contains(new DateOnly(2027, 1, 4)));
    }

    [Fact]
    public void Constructor_RejectsNonMonday()
    {
        Assert.Throws<ArgumentException>(() => new WeekPeriod(new DateOnly(2026, 10, 30)));
    }

    [Fact]
    public void FromDate_RejectsAnUnrepresentableExclusiveEnd()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WeekPeriod.FromDate(DateOnly.MaxValue));
    }

    [Fact]
    public void FromDate_SupportsTheMinimumDate()
    {
        var week = WeekPeriod.FromDate(DateOnly.MinValue);

        Assert.Equal(DateOnly.MinValue, week.Start);
        Assert.True(week.Contains(DateOnly.MinValue));
    }
}
