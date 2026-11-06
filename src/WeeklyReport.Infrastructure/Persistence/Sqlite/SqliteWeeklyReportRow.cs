// [新增-离线存储] Phase 1 切片 C：SQLite 版周报行映射——周起止与时间戳为 ADR-05 固定格式 TEXT。
using SqlSugar;

namespace WeeklyReport.Infrastructure.Persistence.Sqlite;

[SugarTable("weekly_reports")]
public sealed class SqliteWeeklyReportRow
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)]
    public string Id { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "week_start")]
    public string WeekStart { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "week_end")]
    public string WeekEnd { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "work_summary")]
    public string WorkSummary { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "next_week_plan")]
    public string NextWeekPlan { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "risks")]
    public string Risks { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "confirmed_minutes")]
    public int ConfirmedMinutes { get; set; }

    [SugarColumn(ColumnName = "estimated_minutes")]
    public int EstimatedMinutes { get; set; }

    [SugarColumn(ColumnName = "unknown_hours_count")]
    public int UnknownHoursCount { get; set; }

    [SugarColumn(ColumnName = "created_at_utc")]
    public string CreatedAtUtc { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "updated_at_utc")]
    public string UpdatedAtUtc { get; set; } = string.Empty;
}
