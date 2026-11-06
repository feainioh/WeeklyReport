using WeeklyReport.Domain;

namespace WeeklyReport.UnitTests;

public class StorageDateRangeTests
{
    [Fact]
    public void GetNextTimestamp_UsesCurrentTimeWhenItIsLatest()
    {
        var previous = new DateTimeOffset(2026, 11, 5, 8, 0, 0, TimeSpan.Zero);
        var candidate = previous.AddHours(1);

        var result = StorageDateRange.GetNextTimestamp(previous, previous, candidate);

        Assert.Equal(candidate, result);
    }

    [Fact]
    public void GetNextTimestamp_AdvancesPreviousTimestampByOneMicrosecond()
    {
        var previous = new DateTimeOffset(2026, 11, 5, 8, 0, 0, TimeSpan.Zero);

        var result = StorageDateRange.GetNextTimestamp(previous, previous, previous);

        Assert.Equal(previous.AddTicks(TimeSpan.TicksPerMicrosecond), result);
    }

    [Fact]
    public void GetNextTimestamp_PreservesImportedCreationTimeWhenItIsLatest()
    {
        var previous = new DateTimeOffset(2026, 11, 5, 8, 0, 0, TimeSpan.Zero);
        var importedCreation = previous.AddHours(2);
        var candidate = previous.AddHours(1);

        var result = StorageDateRange.GetNextTimestamp(previous, importedCreation, candidate);

        Assert.Equal(importedCreation, result);
    }

    [Fact]
    public void GetNextTimestamp_NormalizesCurrentTimeToMicrosecondPrecision()
    {
        var previous = new DateTimeOffset(2026, 11, 5, 8, 0, 0, TimeSpan.Zero);
        var candidate = previous.AddHours(1).AddTicks(9);

        var result = StorageDateRange.GetNextTimestamp(previous, previous, candidate);

        Assert.Equal(previous.AddHours(1), result);
    }

    [Fact]
    public void GetUpdateTimestamp_RejectsClockBeforePreviousUpdate()
    {
        var previous = new DateTimeOffset(2026, 11, 5, 8, 0, 0, TimeSpan.Zero);

        Assert.Throws<InvalidOperationException>(
            () => StorageDateRange.GetUpdateTimestamp(previous, previous.AddTicks(-1), "记录"));
    }

    [Fact]
    public void GetUpdateTimestamp_AdvancesWhenCurrentTimeMatchesPreviousUpdate()
    {
        var previous = new DateTimeOffset(2026, 11, 5, 8, 0, 0, TimeSpan.Zero);

        var result = StorageDateRange.GetUpdateTimestamp(previous, previous, "记录");

        Assert.Equal(previous.AddTicks(TimeSpan.TicksPerMicrosecond), result);
    }
}
