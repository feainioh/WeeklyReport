// [新增-离线存储] Phase 1 切片 B：SQLite 版 Git 工作项行映射——work_date 为固定格式 TEXT（ADR-05），
// 其余列（布尔/整数）由 SQLite 亲和类型天然承载；复用父命名空间的全字符串 GitProjectRow/GitCommitRow。
using SqlSugar;

namespace WeeklyReport.Infrastructure.Persistence.Sqlite;

[SugarTable("git_work_items")]
public sealed class SqliteGitWorkItemRow
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)]
    public string Id { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "project_id")]
    public string ProjectId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "work_date")]
    public string WorkDate { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "description")]
    public string Description { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "estimated_minutes", IsNullable = true)]
    public int? EstimatedMinutes { get; set; }

    [SugarColumn(ColumnName = "confirmed_minutes", IsNullable = true)]
    public int? ConfirmedMinutes { get; set; }

    [SugarColumn(ColumnName = "is_deleted")]
    public bool IsDeleted { get; set; }

    [SugarColumn(ColumnName = "revision")]
    public long Revision { get; set; }
}
