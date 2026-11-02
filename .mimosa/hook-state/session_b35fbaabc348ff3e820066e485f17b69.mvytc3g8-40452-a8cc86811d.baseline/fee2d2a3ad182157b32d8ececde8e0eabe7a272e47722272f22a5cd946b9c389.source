using WeeklyReport.Domain;

namespace WeeklyReport.Application;

public interface IWorkEntryRepository
{
    Task<IReadOnlyList<WorkEntry>> GetByWeekAsync(WeekPeriod week);
    Task<WorkEntry?> FindAsync(Guid id);
    Task AddAsync(WorkEntry entry);
    Task<bool> UpdateAsync(WorkEntry entry);
    Task<bool> DeleteAsync(Guid id);
}
