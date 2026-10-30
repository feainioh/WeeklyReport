using WeeklyReport.Domain;

namespace WeeklyReport.Application;

public sealed class WeeklyReportDraftBuilder
{
    public WeeklyReportDraft Build(
        WeekPeriod week,
        IEnumerable<WorkEntry> manualEntries,
        IEnumerable<GitReportWorkItem> gitItems)
    {
        ArgumentNullException.ThrowIfNull(manualEntries);
        ArgumentNullException.ThrowIfNull(gitItems);

        var lines = new List<ReportLine>();
        var ids = new HashSet<Guid>();
        long confirmed = 0;
        long estimated = 0;
        var unknown = 0;
        foreach (var entry in manualEntries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (!week.Contains(entry.WorkDate))
            {
                continue;
            }

            if (!ids.Add(entry.Id))
            {
                throw new ArgumentException("周报来源包含重复的工作项标识。", nameof(manualEntries));
            }

            lines.Add(new ReportLine(entry.WorkDate, entry.Id, entry.Content));
            if (entry.ConfirmedMinutes is int minutes)
            {
                confirmed += minutes;
            }
            else
            {
                unknown++;
            }
        }

        foreach (var item in gitItems)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (!week.Contains(item.WorkDate))
            {
                continue;
            }

            if (!ids.Add(item.Id))
            {
                throw new ArgumentException("周报来源包含重复的工作项标识。", nameof(gitItems));
            }

            lines.Add(new ReportLine(item.WorkDate, item.Id, $"[{item.ProjectName}] {item.Description}"));
            if (item.ConfirmedMinutes is int confirmedMinutes)
            {
                confirmed += confirmedMinutes;
            }
            else if (item.EstimatedMinutes is int estimatedMinutes)
            {
                estimated += estimatedMinutes;
            }
            else
            {
                unknown++;
            }
        }

        if (confirmed > int.MaxValue || estimated > int.MaxValue)
        {
            throw new InvalidOperationException("周报工时总计超出支持范围。");
        }

        var summary = string.Join(
            Environment.NewLine,
            lines.OrderBy(line => line.Date).ThenBy(line => line.Id)
                .Select(line => $"{line.Date:yyyy-MM-dd} {line.Content}"));
        return new WeeklyReportDraft(week, summary, (int)confirmed, (int)estimated, unknown);
    }

    private sealed record ReportLine(DateOnly Date, Guid Id, string Content);
}
