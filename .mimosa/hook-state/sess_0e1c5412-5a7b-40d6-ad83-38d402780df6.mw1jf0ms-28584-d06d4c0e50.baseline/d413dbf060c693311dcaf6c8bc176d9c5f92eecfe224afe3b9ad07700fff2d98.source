using WeeklyReport.Domain;

namespace WeeklyReport.Application;

public interface IGitReportWorkItemSource
{
    Task<IReadOnlyList<GitReportWorkItem>> GetByWeekAsync(WeekPeriod week);
}
