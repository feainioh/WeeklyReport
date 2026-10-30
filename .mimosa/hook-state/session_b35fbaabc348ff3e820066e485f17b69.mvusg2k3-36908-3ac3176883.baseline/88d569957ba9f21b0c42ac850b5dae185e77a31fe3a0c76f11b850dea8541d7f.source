namespace WeeklyReport.Domain;

public sealed class WorkEntry
{
    public WorkEntry(
        Guid id,
        DateOnly workDate,
        string content,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        int? confirmedMinutes = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("The work entry must have an identifier.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        StorageDateRange.ValidateWorkDate(workDate);
        StorageDateRange.ValidateUtcTimestamp(createdAtUtc);
        StorageDateRange.ValidateUtcTimestamp(updatedAtUtc);

        if (updatedAtUtc < createdAtUtc)
        {
            throw new ArgumentException("The update time cannot precede creation.", nameof(updatedAtUtc));
        }

        if (confirmedMinutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(confirmedMinutes), "工时不能为负数。");
        }

        Id = id;
        WorkDate = workDate;
        Content = content.Trim();
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        UpdatedAtUtc = updatedAtUtc.ToUniversalTime();
        ConfirmedMinutes = confirmedMinutes;
    }

    public Guid Id { get; }
    public DateOnly WorkDate { get; }
    public string Content { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; }
    public int? ConfirmedMinutes { get; }
    public decimal? ConfirmedHours => ConfirmedMinutes / 60m;
}
