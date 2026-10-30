namespace WeeklyReport.Domain;

public sealed class DatedGitCommit
{
    public DatedGitCommit(GitCommit commit, DateOnly workDate)
    {
        ArgumentNullException.ThrowIfNull(commit);
        Commit = commit;
        WorkDate = workDate;
    }

    public GitCommit Commit { get; }
    public DateOnly WorkDate { get; }
}
