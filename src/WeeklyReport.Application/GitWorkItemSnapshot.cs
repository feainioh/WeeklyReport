using WeeklyReport.Domain;

namespace WeeklyReport.Application;

public sealed class GitWorkItemSnapshot
{
    public GitWorkItemSnapshot(
        Guid id,
        Guid projectId,
        DateOnly workDate,
        string description,
        int? confirmedMinutes,
        bool isDeleted,
        IEnumerable<GitCommit> commits,
        int? estimatedMinutes = null,
        long revision = 0)
    {
        if (id == Guid.Empty || projectId == Guid.Empty)
        {
            throw new ArgumentException("工作项和项目标识不能为空。");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        StorageDateRange.ValidateWorkDate(workDate);
        ArgumentNullException.ThrowIfNull(commits);
        if (confirmedMinutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(confirmedMinutes));
        }

        if (estimatedMinutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(estimatedMinutes));
        }

        if (revision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision));
        }

        var storedCommits = commits.ToArray();
        if (storedCommits.Any(commit => commit is null || commit.ProjectId != projectId))
        {
            throw new ArgumentException("提交必须属于工作项的项目。", nameof(commits));
        }

        Id = id;
        ProjectId = projectId;
        WorkDate = workDate;
        Description = description;
        ConfirmedMinutes = confirmedMinutes;
        EstimatedMinutes = estimatedMinutes;
        IsDeleted = isDeleted;
        Revision = revision;
        Commits = Array.AsReadOnly(storedCommits);
    }

    public Guid Id { get; }
    public Guid ProjectId { get; }
    public DateOnly WorkDate { get; }
    public string Description { get; }
    public int? ConfirmedMinutes { get; }
    public int? EstimatedMinutes { get; }
    public bool IsDeleted { get; }
    public long Revision { get; }
    public IReadOnlyList<GitCommit> Commits { get; }
}
