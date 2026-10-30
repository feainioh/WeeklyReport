using System.Diagnostics.CodeAnalysis;
using System.Text;
using WeeklyReport.Domain;

namespace WeeklyReport.Application;

public static class BackupValidator
{
    private const int MediumTextMaximumBytes = 16_777_215;
    private static readonly UTF8Encoding Utf8 = new(false, throwOnInvalidBytes: true);

    public static void Validate(BackupDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Require(document.FormatVersion == BackupDocument.CurrentFormatVersion, "备份格式版本不受支持。");
        Require(document.ExportedAtUtc.Offset == TimeSpan.Zero, "导出时间必须为 UTC。");
        Require(document.Projects is not null && document.Entries is not null
            && document.Commits is not null && document.Reports is not null, "备份缺少必要数据表。");

        var projects = new Dictionary<Guid, BackupProject>();
        foreach (var project in document.Projects)
        {
            Require(project is not null && project.Id != Guid.Empty, "项目标识无效。");
            Require(!string.IsNullOrWhiteSpace(project.Name) && project.Name.Length <= 200
                && project.Name == project.Name.Trim(), "项目名称无效。");
            ValidateText(project.Name, "项目名称");
            Require(projects.TryAdd(project.Id, project), "备份包含重复的项目标识。");
        }

        var entries = new Dictionary<Guid, BackupEntry>();
        var gitDays = new HashSet<(Guid ProjectId, DateOnly Date)>();
        foreach (var entry in document.Entries)
        {
            Require(entry is not null && entry.Id != Guid.Empty, "工作项标识无效。");
            Require(Enum.IsDefined(entry.Kind), "工作项类型无效。");
            Require(!string.IsNullOrWhiteSpace(entry.Description)
                && entry.Description == entry.Description.Trim(), "工作项描述无效。");
            ValidateText(entry.Description, "工作项描述");
            Require(entry.EstimatedMinutes is null or >= 0 && entry.ConfirmedMinutes is null or >= 0,
                "工作项工时不能为负数。");
            Require(entries.TryAdd(entry.Id, entry), "备份包含重复的工作项标识。");
            ValidateWeekDate(entry.WorkDate);

            if (entry.Kind == BackupEntryKind.Manual)
            {
                Require(entry.ProjectId is null && entry.EstimatedMinutes is null && !entry.IsDeleted,
                    "手工工作项包含无效的 Git 字段。");
                Require(entry.CreatedAtUtc is not null && entry.UpdatedAtUtc is not null,
                    "手工工作项缺少创建或更新时间。");
                ValidateUtcTimes(entry.CreatedAtUtc!.Value, entry.UpdatedAtUtc!.Value);
            }
            else
            {
                Require(entry.ProjectId is Guid projectId && projects.ContainsKey(projectId),
                    "Git 工作项关联的项目不存在。");
                Require(entry.CreatedAtUtc is null && entry.UpdatedAtUtc is null,
                    "Git 工作项包含不支持的时间字段。");
                Require(gitDays.Add((entry.ProjectId!.Value, entry.WorkDate)),
                    "备份包含重复的项目日期工作项。");
                if (entry.IsDeleted)
                {
                    Require(entry.Description == "（已删除）" && entry.EstimatedMinutes is null
                        && entry.ConfirmedMinutes is null, "已删除工作项的保留状态无效。");
                }
            }
        }

        var commits = new HashSet<(Guid ProjectId, string Hash)>();
        var linkedItems = new HashSet<Guid>();
        foreach (var commit in document.Commits)
        {
            Require(commit is not null && projects.ContainsKey(commit.ProjectId), "Git 提交关联的项目不存在。");
            Require(entries.TryGetValue(commit.WorkItemId, out var item)
                && item.Kind == BackupEntryKind.Git && item.ProjectId == commit.ProjectId,
                "Git 提交关联的工作项无效。");
            Require(commit.Hash is not null && commit.Hash.Length is 40 or 64
                && commit.Hash.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'),
                "Git 提交哈希必须是小写完整哈希。");
            Require(commits.Add((commit.ProjectId, commit.Hash)), "备份包含重复的 Git 提交。");
            Require(commit.AuthorName is not null && commit.AuthorEmail is not null && commit.Subject is not null,
                "Git 提交字段缺失。");
            ValidateText(commit.AuthorName, "Git 作者");
            ValidateText(commit.AuthorEmail, "Git 作者邮箱");
            ValidateText(commit.Subject, "Git 提交摘要");
            if (item!.IsDeleted)
            {
                Require(commit.AuthorName.Length == 0 && commit.AuthorEmail.Length == 0 && commit.Subject.Length == 0,
                    "已删除工作项的提交内容未清除。");
            }

            linkedItems.Add(commit.WorkItemId);
        }

        Require(entries.Values.Where(entry => entry.Kind == BackupEntryKind.Git).All(entry => linkedItems.Contains(entry.Id)),
            "Git 工作项缺少提交来源。");

        var reportIds = new HashSet<Guid>();
        var weeks = new HashSet<DateOnly>();
        foreach (var report in document.Reports)
        {
            Require(report is not null && report.Id != Guid.Empty && reportIds.Add(report.Id),
                "周报标识无效或重复。");
            WeekPeriod week;
            try
            {
                week = new WeekPeriod(report.WeekStart);
                StorageDateRange.ValidateWorkDate(report.WeekStart);
            }
            catch (ArgumentException exception)
            {
                throw new BackupFormatException("周报起始日期无效。", exception);
            }

            Require(week.EndInclusive == report.WeekEnd && weeks.Add(report.WeekStart), "周报周期无效或重复。");
            Require(report.WorkSummary is not null && report.NextWeekPlan is not null && report.Risks is not null,
                "周报内容字段缺失。");
            ValidateText(report.WorkSummary, "本周工作");
            ValidateText(report.NextWeekPlan, "下周计划");
            ValidateText(report.Risks, "问题与风险");
            Require(report.ConfirmedMinutes >= 0 && report.EstimatedMinutes >= 0 && report.UnknownHoursCount >= 0,
                "周报工时或未填写数量无效。");
            ValidateUtcTimes(report.CreatedAtUtc, report.UpdatedAtUtc);
        }
    }

    private static void ValidateWeekDate(DateOnly date)
    {
        try
        {
            StorageDateRange.ValidateWorkDate(date);
        }
        catch (ArgumentException exception)
        {
            throw new BackupFormatException("工作项日期超出支持范围。", exception);
        }
    }

    private static void ValidateUtcTimes(DateTimeOffset created, DateTimeOffset updated)
    {
        Require(created.Offset == TimeSpan.Zero && updated.Offset == TimeSpan.Zero && updated >= created,
            "创建或更新时间无效，时间必须为 UTC。");
        try
        {
            StorageDateRange.ValidateUtcTimestamp(created);
            StorageDateRange.ValidateUtcTimestamp(updated);
        }
        catch (ArgumentException exception)
        {
            throw new BackupFormatException("创建或更新时间超出 MySQL 范围或精度。", exception);
        }
    }

    private static void ValidateText(string text, string field)
    {
        try
        {
            Require(Utf8.GetByteCount(text) <= MediumTextMaximumBytes, $"{field}超过数据库允许的大小。");
        }
        catch (EncoderFallbackException exception)
        {
            throw new BackupFormatException($"{field}包含无效字符。", exception);
        }
    }

    private static void Require([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition)
        {
            throw new BackupFormatException(message);
        }
    }
}
