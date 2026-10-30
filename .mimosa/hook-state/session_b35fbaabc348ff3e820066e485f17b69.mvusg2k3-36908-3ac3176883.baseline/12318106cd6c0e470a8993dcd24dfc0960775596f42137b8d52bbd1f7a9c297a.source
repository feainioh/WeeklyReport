namespace WeeklyReport.Application;

public interface IGitProjectRepository
{
    Task<IReadOnlyList<GitProject>> ListAsync();
    Task<GitProject?> FindAsync(Guid id);
    Task AddAsync(GitProject project);
    Task<bool> UpdateAsync(GitProject project);
    Task<bool> DeleteAsync(Guid id);
}
