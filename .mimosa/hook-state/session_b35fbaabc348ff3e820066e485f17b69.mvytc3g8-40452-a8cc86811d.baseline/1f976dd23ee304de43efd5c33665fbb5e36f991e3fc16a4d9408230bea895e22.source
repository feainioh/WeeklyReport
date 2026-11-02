using WeeklyReport.Domain;

namespace WeeklyReport.Application;

public sealed class WeeklyReportDraft
{
    public WeeklyReportDraft(
        WeekPeriod week,
        string workSummary,
        int confirmedMinutes,
        int estimatedMinutes,
        int unknownHoursCount)
    {
        ArgumentNullException.ThrowIfNull(workSummary);
        if (confirmedMinutes < 0 || estimatedMinutes < 0 || unknownHoursCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(confirmedMinutes), "工时和未确认数量不能为负数。");
        }

        Week = week;
        WorkSummary = workSummary;
        ConfirmedMinutes = confirmedMinutes;
        EstimatedMinutes = estimatedMinutes;
        UnknownHoursCount = unknownHoursCount;
    }

    public WeekPeriod Week { get; }
    public string WorkSummary { get; }
    public int ConfirmedMinutes { get; }
    public int EstimatedMinutes { get; }
    public int UnknownHoursCount { get; }
}
