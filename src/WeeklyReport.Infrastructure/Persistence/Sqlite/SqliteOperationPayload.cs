using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WeeklyReport.Application;
using WeeklyReport.Domain;

namespace WeeklyReport.Infrastructure.Persistence.Sqlite;

internal static class SqliteOperationPayload
{
    internal const int CurrentVersion = 2;

    internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value);

    internal static string ComputeHash(string changesJson)
    {
        var hashInput = Encoding.UTF8.GetBytes($"{CurrentVersion}\n{changesJson}");
        return Convert.ToHexString(SHA256.HashData(hashInput));
    }

    internal static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json)
        ?? throw new JsonException("操作快照内容为空。");

    internal static string SerializeWeeklyReport(WeeklyReportSnapshot report) =>
        Serialize(SqliteWeeklyReportOperationSnapshot.From(report));

    internal static WeeklyReportSnapshot DeserializeWeeklyReport(string json) =>
        Deserialize<SqliteWeeklyReportOperationSnapshot>(json).ToDomain();

    internal static string SerializeGitWorkItem(GitWorkItemSnapshot item, GitProject project) =>
        Serialize(SqliteGitWorkItemOperationSnapshot.From(item, project));

    internal static SqliteGitWorkItemOperationSnapshot DeserializeGitWorkItem(string json) =>
        Deserialize<SqliteGitWorkItemOperationSnapshot>(json);
}

internal sealed record SqliteGitWorkItemOperationSnapshot(
    SqliteGitWorkItemSnapshot WorkItem,
    GitProject Project)
{
    internal static SqliteGitWorkItemOperationSnapshot From(GitWorkItemSnapshot item, GitProject project) =>
        new(
            new SqliteGitWorkItemSnapshot(
                item.Id,
                item.ProjectId,
                item.WorkDate,
                item.Description,
                item.ConfirmedMinutes,
                item.IsDeleted,
                item.Commits.ToArray(),
                item.EstimatedMinutes,
                item.Revision),
            project);
}

internal sealed record SqliteGitWorkItemSnapshot(
    Guid Id,
    Guid ProjectId,
    DateOnly WorkDate,
    string Description,
    int? ConfirmedMinutes,
    bool IsDeleted,
    GitCommit[] Commits,
    int? EstimatedMinutes,
    long Revision)
{
    internal GitWorkItemSnapshot ToDomain() =>
        new(
            Id,
            ProjectId,
            WorkDate,
            Description,
            ConfirmedMinutes,
            IsDeleted,
            Commits,
            EstimatedMinutes,
            Revision);
}

internal sealed record SqliteWeeklyReportOperationSnapshot(
    Guid Id,
    DateOnly WeekStart,
    string WorkSummary,
    string NextWeekPlan,
    string Risks,
    int ConfirmedMinutes,
    int EstimatedMinutes,
    int UnknownHoursCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    internal static SqliteWeeklyReportOperationSnapshot From(WeeklyReportSnapshot report) =>
        new(
            report.Id,
            report.Week.Start,
            report.WorkSummary,
            report.NextWeekPlan,
            report.Risks,
            report.ConfirmedMinutes,
            report.EstimatedMinutes,
            report.UnknownHoursCount,
            report.CreatedAtUtc,
            report.UpdatedAtUtc);

    internal WeeklyReportSnapshot ToDomain() =>
        new(
            Id,
            new WeekPeriod(WeekStart),
            WorkSummary,
            NextWeekPlan,
            Risks,
            ConfirmedMinutes,
            EstimatedMinutes,
            UnknownHoursCount,
            CreatedAtUtc,
            UpdatedAtUtc);
}
