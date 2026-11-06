namespace WeeklyReport.Domain;

public static class StorageDateRange
{
    public static void ValidateWorkDate(DateOnly date)
    {
        var week = WeekPeriod.FromDate(date);
        if (week.Start.Year < 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(date), "工作日期超出 MySQL 支持范围。");
        }
    }

    public static void ValidateUtcTimestamp(DateTimeOffset value)
    {
        if (value.UtcDateTime.Year < 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "时间超出 MySQL 支持范围。");
        }

        if (value.UtcDateTime.Ticks % 10 != 0)
        {
            throw new ArgumentException("时间必须精确到微秒。", nameof(value));
        }
    }

    public static DateTimeOffset NormalizeUtcTimestamp(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        var ticks = utc.Ticks - utc.Ticks % TimeSpan.TicksPerMicrosecond;
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    public static DateTimeOffset GetNextTimestamp(
        DateTimeOffset previous,
        DateTimeOffset minimum,
        DateTimeOffset candidate)
    {
        var next = previous.AddTicks(TimeSpan.TicksPerMicrosecond);
        var normalizedCandidate = NormalizeUtcTimestamp(candidate);
        if (normalizedCandidate < next)
        {
            normalizedCandidate = next;
        }

        return normalizedCandidate < minimum ? minimum : normalizedCandidate;
    }

    public static DateTimeOffset GetUpdateTimestamp(
        DateTimeOffset previous,
        DateTimeOffset candidate,
        string entityName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);
        var normalizedCandidate = NormalizeUtcTimestamp(candidate);
        if (normalizedCandidate < previous)
        {
            throw new InvalidOperationException($"系统时间早于{entityName}更新时间，请检查系统时钟。");
        }

        return normalizedCandidate == previous
            ? previous.AddTicks(TimeSpan.TicksPerMicrosecond)
            : normalizedCandidate;
    }
}
