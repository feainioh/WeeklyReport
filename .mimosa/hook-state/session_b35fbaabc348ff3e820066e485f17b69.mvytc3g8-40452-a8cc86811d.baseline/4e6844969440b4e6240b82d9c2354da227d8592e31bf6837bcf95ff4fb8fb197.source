namespace WeeklyReport.Domain;

public sealed class WeeklyReportSnapshot
{
    public WeeklyReportSnapshot(
        Guid id,
        WeekPeriod week,
        string workSummary,
        string nextWeekPlan,
        string risks,
        int confirmedMinutes,
        int estimatedMinutes,
        int unknownHoursCount,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("周报标识不能为空。", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(workSummary);
        ArgumentNullException.ThrowIfNull(nextWeekPlan);
        ArgumentNullException.ThrowIfNull(risks);
        StorageDateRange.ValidateWorkDate(week.Start);
        StorageDateRange.ValidateUtcTimestamp(createdAtUtc);
        StorageDateRange.ValidateUtcTimestamp(updatedAtUtc);
        if (confirmedMinutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(confirmedMinutes), "确认工时不能为负数。");
        }

        if (estimatedMinutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(estimatedMinutes), "估算工时不能为负数。");
        }

        if (unknownHoursCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unknownHoursCount), "未确认数量不能为负数。");
        }

        if (updatedAtUtc < createdAtUtc)
        {
            throw new ArgumentException("更新时间不能早于创建时间。", nameof(updatedAtUtc));
        }

        Id = id;
        Week = week;
        WorkSummary = workSummary;
        NextWeekPlan = nextWeekPlan;
        Risks = risks;
        ConfirmedMinutes = confirmedMinutes;
        EstimatedMinutes = estimatedMinutes;
        UnknownHoursCount = unknownHoursCount;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        UpdatedAtUtc = updatedAtUtc.ToUniversalTime();
    }

    public Guid Id { get; }
    public WeekPeriod Week { get; }
    public string WorkSummary { get; }
    public string NextWeekPlan { get; }
    public string Risks { get; }
    public int ConfirmedMinutes { get; }
    public int EstimatedMinutes { get; }
    public int UnknownHoursCount { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; }
}
