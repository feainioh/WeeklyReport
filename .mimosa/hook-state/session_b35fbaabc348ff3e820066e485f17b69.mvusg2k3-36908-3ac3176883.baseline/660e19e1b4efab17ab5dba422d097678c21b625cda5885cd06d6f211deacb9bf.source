using SqlSugar;

namespace WeeklyReport.Infrastructure.Persistence;

[SugarTable("weekly_reports")]
public sealed class WeeklyReportRow
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)]
    public string Id { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "week_start")]
    public DateTime WeekStart { get; set; }

    [SugarColumn(ColumnName = "week_end")]
    public DateTime WeekEnd { get; set; }

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
    public DateTime CreatedAtUtc { get; set; }

    [SugarColumn(ColumnName = "updated_at_utc")]
    public DateTime UpdatedAtUtc { get; set; }
}
