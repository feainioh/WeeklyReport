using WeeklyReport.Domain;

namespace WeeklyReport.Application;

public interface IWorkEntryRepository
{
    Task<IReadOnlyList<WorkEntry>> GetByWeekAsync(WeekPeriod week);
    Task<WorkEntry?> FindAsync(Guid id);
    Task AddAsync(WorkEntry entry);
    Task<bool> UpdateAsync(WorkEntry entry);
    Task<bool> DeleteAsync(Guid id);

    // [新增-离线存储] Phase 1 切片 E：全量列举（首次镜像需要读取全部工作记录，现有按周查询无法覆盖跨周数据）
    Task<IReadOnlyList<WorkEntry>> ListAllAsync();
}
