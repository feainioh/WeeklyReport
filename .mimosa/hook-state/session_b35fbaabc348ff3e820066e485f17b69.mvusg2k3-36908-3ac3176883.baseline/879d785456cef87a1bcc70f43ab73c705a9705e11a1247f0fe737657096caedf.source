using WeeklyReport.Domain;

namespace WeeklyReport.Application;

public static class BackupSelection
{
    public static BackupDocument SelectWeeks(BackupDocument source, IEnumerable<DateOnly> weekStarts)
    {
        BackupValidator.Validate(source);
        ArgumentNullException.ThrowIfNull(weekStarts);
        var selected = weekStarts.ToHashSet();
        if (selected.Count == 0)
        {
            throw new ArgumentException("请至少选择一周周报。", nameof(weekStarts));
        }

        var available = source.Reports.Select(report => report.WeekStart).ToHashSet();
        if (!selected.IsSubsetOf(available))
        {
            throw new ArgumentException("所选周报不存在，请刷新后重试。", nameof(weekStarts));
        }

        var entries = source.Entries.Where(entry => selected.Contains(WeekPeriod.FromDate(entry.WorkDate).Start)).ToArray();
        var entryIds = entries.Select(entry => entry.Id).ToHashSet();
        var projectIds = entries.Where(entry => entry.ProjectId is not null)
            .Select(entry => entry.ProjectId!.Value).ToHashSet();
        var result = new BackupDocument(
            source.FormatVersion,
            source.ExportedAtUtc,
            source.Projects.Where(project => projectIds.Contains(project.Id)).ToArray(),
            entries,
            source.Commits.Where(commit => entryIds.Contains(commit.WorkItemId)).ToArray(),
            source.Reports.Where(report => selected.Contains(report.WeekStart)).ToArray());
        BackupValidator.Validate(result);
        return result;
    }
}
