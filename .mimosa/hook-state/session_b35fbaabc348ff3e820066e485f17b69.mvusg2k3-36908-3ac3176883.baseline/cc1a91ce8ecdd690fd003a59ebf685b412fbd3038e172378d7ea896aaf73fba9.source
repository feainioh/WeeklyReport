using WeeklyReport.Domain;

namespace WeeklyReport.Application;

public sealed record GitReadResult(IReadOnlyList<GitCommit> Commits, bool HasHead, bool IsShallow);

public interface IGitCommitReader
{
    Task<GitReadResult> ReadAsync(GitRepositoryQuery query, CancellationToken cancellationToken = default);
}
