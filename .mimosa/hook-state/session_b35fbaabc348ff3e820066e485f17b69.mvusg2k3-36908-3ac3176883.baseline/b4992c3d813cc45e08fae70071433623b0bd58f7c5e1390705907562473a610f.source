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
        return new DateTimeOffset(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero);
    }
}
