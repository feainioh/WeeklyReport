using SqlSugar;

namespace WeeklyReport.Infrastructure.Persistence;

[SugarTable("work_entries")]
public sealed class WorkEntryRow
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)]
    public string Id { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "work_date")]
    public DateTime WorkDate { get; set; }

    [SugarColumn(ColumnName = "content")]
    public string Content { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "created_at_utc")]
    public DateTime CreatedAtUtc { get; set; }

    [SugarColumn(ColumnName = "updated_at_utc")]
    public DateTime UpdatedAtUtc { get; set; }

    [SugarColumn(ColumnName = "confirmed_minutes", IsNullable = true)]
    public int? ConfirmedMinutes { get; set; }
}
