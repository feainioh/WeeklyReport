namespace WeeklyReport.Application;

public interface IGitSyncStore : IGitReportWorkItemSource
{
    Task<IReadOnlyList<GitWorkItemSnapshot>> LoadAllAsync();
    Task<GitSyncApplyResult> ApplyAsync(PreparedGitSync sync);
    Task<bool> UpdateWorkItemAsync(Guid id, string description, int? confirmedMinutes);
    Task<bool> SoftDeleteWorkItemAsync(Guid id);
    Task<int> RecalculateDateAsync(DateOnly date);
}
