// [新增-离线存储] Phase 1 切片 A：SQLite 工作记录行映射——日期/时间戳为固定格式 TEXT
// （ADR-05：微秒精度 ISO8601；不用 SqlSugar DateTime 映射以免毫秒截断）。
using SqlSugar;

namespace WeeklyReport.Infrastructure.Persistence.Sqlite;

[SugarTable("work_entries")]
public sealed class SqliteWorkEntryRow
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)]
    public string Id { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "work_date")]
    public string WorkDate { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "content")]
    public string Content { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "created_at_utc")]
    public string CreatedAtUtc { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "updated_at_utc")]
    public string UpdatedAtUtc { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "confirmed_minutes", IsNullable = true)]
    public int? ConfirmedMinutes { get; set; }
}
