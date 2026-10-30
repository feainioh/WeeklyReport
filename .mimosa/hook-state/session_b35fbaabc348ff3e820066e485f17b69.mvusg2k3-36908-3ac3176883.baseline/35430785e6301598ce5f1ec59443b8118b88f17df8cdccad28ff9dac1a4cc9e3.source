namespace WeeklyReport.Domain;

public sealed class GitReportWorkItem
{
    public GitReportWorkItem(
        Guid id,
        Guid projectId,
        string projectName,
        DateOnly workDate,
        string description,
        int? estimatedMinutes,
        int? confirmedMinutes)
    {
        if (id == Guid.Empty || projectId == Guid.Empty)
        {
            throw new ArgumentException("工作项和项目标识不能为空。");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        StorageDateRange.ValidateWorkDate(workDate);
        if (estimatedMinutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(estimatedMinutes), "估算工时不能为负数。");
        }

        if (confirmedMinutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(confirmedMinutes), "确认工时不能为负数。");
        }

        Id = id;
        ProjectId = projectId;
        ProjectName = projectName.Trim();
        WorkDate = workDate;
        Description = description.Trim();
        EstimatedMinutes = estimatedMinutes;
        ConfirmedMinutes = confirmedMinutes;
    }

    public Guid Id { get; }
    public Guid ProjectId { get; }
    public string ProjectName { get; }
    public DateOnly WorkDate { get; }
    public string Description { get; }
    public int? EstimatedMinutes { get; }
    public int? ConfirmedMinutes { get; }
}
