using WeeklyReport.Domain;

namespace WeeklyReport.Application;

public interface IWeeklyReportRepository
{
    Task<WeeklyReportSnapshot?> FindByWeekAsync(WeekPeriod week);
    Task<IReadOnlyList<WeeklyReportSnapshot>> ListAsync();
    Task AddAsync(WeeklyReportSnapshot report);
    Task<bool> UpdateAsync(WeeklyReportSnapshot report, DateTimeOffset expectedUpdatedAtUtc);
    Task<bool> DeleteAsync(Guid id, DateTimeOffset expectedUpdatedAtUtc);
}
