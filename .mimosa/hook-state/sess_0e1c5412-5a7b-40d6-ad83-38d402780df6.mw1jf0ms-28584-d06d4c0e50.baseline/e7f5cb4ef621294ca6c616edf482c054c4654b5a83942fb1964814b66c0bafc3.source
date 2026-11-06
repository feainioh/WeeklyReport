using SqlSugar;

namespace WeeklyReport.Infrastructure.Persistence;

[SugarTable("git_work_items")]
public sealed class GitWorkItemRow
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)]
    public string Id { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "project_id")]
    public string ProjectId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "work_date")]
    public DateTime WorkDate { get; set; }

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
