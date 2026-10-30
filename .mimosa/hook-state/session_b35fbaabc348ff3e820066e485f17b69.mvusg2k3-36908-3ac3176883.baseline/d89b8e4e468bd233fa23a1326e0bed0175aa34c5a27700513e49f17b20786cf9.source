using WeeklyReport.Domain;

namespace WeeklyReport.Application;

public sealed record GitProjectReadStatus(Guid ProjectId, bool HasHead, bool IsShallow);

public sealed class PreparedGitSync
{
    internal PreparedGitSync(
        IReadOnlyList<GitProject> projects,
        IReadOnlyList<GitWorkItemSnapshot> baseline,
        IReadOnlyList<GitProjectReadStatus> statuses,
        GitSyncPreview preview)
    {
        Projects = projects;
        Baseline = baseline;
        Statuses = statuses;
        Preview = preview;
    }

    public IReadOnlyList<GitProject> Projects { get; }
    public IReadOnlyList<GitWorkItemSnapshot> Baseline { get; }
    public IReadOnlyList<GitProjectReadStatus> Statuses { get; }
    public GitSyncPreview Preview { get; }
}

public sealed record GitSyncApplyResult(int Created, int Updated, int DuplicateCount, int DeletedConflictCount);

public sealed class GitSyncService(IGitCommitReader reader, IGitSyncStore store, GitSyncPlanner planner)
{
    public async Task<PreparedGitSync> PrepareAsync(
        IEnumerable<GitProject> projects,
        DateOnly start,
        DateOnly end,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(timeZone);
        var selected = projects.ToArray();
        if (selected.Length == 0 || selected.Any(project => project is null))
        {
            throw new ArgumentException("请至少选择一个 Git 项目。", nameof(projects));
        }

        if (selected.Select(project => project.Id).Distinct().Count() != selected.Length)
        {
            throw new ArgumentException("不能重复选择同一个 Git 项目。", nameof(projects));
        }

        var commits = new List<GitCommit>();
        var statuses = new List<GitProjectReadStatus>();
        foreach (var project in selected)
        {
            if (project.RepositoryPath is null || project.AuthorField is null || project.Author is null)
            {
                throw new InvalidOperationException("所选项目尚未绑定本地仓库和作者。");
            }

            var query = new GitRepositoryQuery(
                project.Id, project.RepositoryPath, project.AuthorField.Value, project.Author, start, end, timeZone);
            var result = await reader.ReadAsync(query, cancellationToken);
            commits.AddRange(result.Commits);
            statuses.Add(new GitProjectReadStatus(project.Id, result.HasHead, result.IsShallow));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var baseline = await store.LoadAllAsync();
        cancellationToken.ThrowIfCancellationRequested();
        var preview = planner.CreatePreview(baseline, commits, timeZone);
        return new PreparedGitSync(
            Array.AsReadOnly(selected),
            Array.AsReadOnly(baseline.ToArray()),
            statuses.AsReadOnly(),
            preview);
    }

    public Task<GitSyncApplyResult> ApplyAsync(
        PreparedGitSync sync,
        bool userConfirmed,
        bool skipDeletedConflicts,
        bool acceptShallowHistory)
    {
        ArgumentNullException.ThrowIfNull(sync);
        if (!userConfirmed)
        {
            throw new InvalidOperationException("应用 Git 同步结果前必须由用户确认。");
        }

        if (sync.Preview.DeletedConflicts.Count > 0 && !skipDeletedConflicts)
        {
            throw new InvalidOperationException("存在已删除工作项，请明确选择跳过后再应用。");
        }

        if (sync.Statuses.Any(status => status.IsShallow) && !acceptShallowHistory)
        {
            throw new InvalidOperationException("所选仓库包含不完整历史，请确认后再应用。");
        }

        return store.ApplyAsync(sync);
    }
}
