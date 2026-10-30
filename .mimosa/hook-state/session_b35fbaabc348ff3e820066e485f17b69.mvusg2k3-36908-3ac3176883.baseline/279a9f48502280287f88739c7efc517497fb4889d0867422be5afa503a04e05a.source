namespace WeeklyReport.Application;

public enum BackupConflictPolicy
{
    Skip,
    Overwrite
}

public sealed record BackupConflictSummary(
    int Projects,
    int ManualEntries,
    int GitWorkItems,
    int GitCommits,
    int Reports,
    int RestoredGitWorkItems);

public sealed class PreparedBackupImport
{
    internal PreparedBackupImport(BackupDocument incoming, BackupDocument baseline, BackupConflictSummary conflicts)
    {
        Incoming = incoming;
        Baseline = baseline;
        Conflicts = conflicts;
    }

    public BackupDocument Incoming { get; }
    public BackupDocument Baseline { get; }
    public BackupConflictSummary Conflicts { get; }
}

public sealed class BackupImportPlanner
{
    public PreparedBackupImport Prepare(BackupDocument incoming, BackupDocument current)
    {
        BackupValidator.Validate(incoming);
        BackupValidator.Validate(current);
        incoming = Copy(incoming);
        current = Copy(current);

        var existingProjects = current.Projects.ToDictionary(item => item.Id);
        var existingEntries = current.Entries.ToDictionary(item => item.Id);
        var existingGitDays = current.Entries.Where(item => item.Kind == BackupEntryKind.Git)
            .ToDictionary(item => (item.ProjectId!.Value, item.WorkDate));
        var existingCommits = current.Commits.ToDictionary(item => (item.ProjectId, item.Hash));
        var existingReportsById = current.Reports.ToDictionary(item => item.Id);
        var existingReportsByWeek = current.Reports.ToDictionary(item => item.WeekStart);
        var incomingEntries = incoming.Entries.ToDictionary(item => item.Id);

        var manualConflicts = 0;
        var gitConflicts = 0;
        var restored = 0;
        foreach (var item in incoming.Entries)
        {
            if (existingEntries.TryGetValue(item.Id, out var sameId)
                && (sameId.Kind != item.Kind
                    || (item.Kind == BackupEntryKind.Git
                        && (sameId.ProjectId != item.ProjectId || sameId.WorkDate != item.WorkDate))))
            {
                throw new BackupFormatException("工作项标识与现有数据的类型或归属不一致，无法导入。");
            }

            if (item.Kind == BackupEntryKind.Manual)
            {
                if (sameId is not null)
                {
                    manualConflicts++;
                }
            }
            else if (existingGitDays.TryGetValue((item.ProjectId!.Value, item.WorkDate), out var sameDay))
            {
                gitConflicts++;
                if (sameDay.IsDeleted && !item.IsDeleted)
                {
                    restored++;
                }
            }
        }

        foreach (var report in incoming.Reports)
        {
            if (existingReportsById.TryGetValue(report.Id, out var sameId)
                && sameId.WeekStart != report.WeekStart)
            {
                throw new BackupFormatException("周报标识与现有周次不一致，无法导入。");
            }
        }

        var commitConflicts = 0;
        foreach (var commit in incoming.Commits)
        {
            if (!existingCommits.TryGetValue((commit.ProjectId, commit.Hash), out var existing))
            {
                continue;
            }

            commitConflicts++;
            var incomingItem = incomingEntries[commit.WorkItemId];
            var existingItem = existingEntries[existing.WorkItemId];
            if (incomingItem.ProjectId != existingItem.ProjectId || incomingItem.WorkDate != existingItem.WorkDate)
            {
                throw new BackupFormatException("Git 提交已关联到其他工作日期，无法安全导入。");
            }

            if (!incomingItem.IsDeleted && !existingItem.IsDeleted
                && (commit.AuthorName != existing.AuthorName
                    || commit.AuthorEmail != existing.AuthorEmail
                    || !commit.AuthoredAt.EqualsExact(existing.AuthoredAt)
                    || commit.Subject != existing.Subject))
            {
                throw new BackupFormatException("相同 Git 提交的内容不一致，无法安全导入。");
            }
        }

        var conflicts = new BackupConflictSummary(
            incoming.Projects.Count(item => existingProjects.ContainsKey(item.Id)),
            manualConflicts,
            gitConflicts,
            commitConflicts,
            incoming.Reports.Count(item => existingReportsByWeek.ContainsKey(item.WeekStart)),
            restored);
        return new PreparedBackupImport(incoming, current, conflicts);
    }

    private static BackupDocument Copy(BackupDocument source) => source with
    {
        Projects = Array.AsReadOnly(source.Projects.ToArray()),
        Entries = Array.AsReadOnly(source.Entries.ToArray()),
        Commits = Array.AsReadOnly(source.Commits.ToArray()),
        Reports = Array.AsReadOnly(source.Reports.ToArray())
    };
}
