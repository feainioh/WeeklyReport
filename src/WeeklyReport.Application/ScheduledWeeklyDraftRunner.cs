namespace WeeklyReport.Application;

public sealed class ScheduledWeeklyDraftRunner(WeeklyReportService reports) : IScheduledTaskRunner
{
    public async Task<ScheduledTaskOutcome> RunAsync(
        ScheduledTaskKind kind,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        if (kind != ScheduledTaskKind.WeeklyDraft)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "周报执行器不支持该任务类型。");
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var result = await reports.OpenOrCreateDraftAsync(date);
            if (result.Existing is not null)
            {
                return ScheduledTaskOutcome.Skipped("本周周报已存在，未覆盖。");
            }

            var draft = result.Draft
                ?? throw new InvalidOperationException("周报服务未返回已有周报或草稿。");
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(draft.WorkSummary))
            {
                return ScheduledTaskOutcome.Skipped("本周暂无工作项，未生成周报。");
            }

            await reports.SaveNewAsync(draft, draft.WorkSummary, string.Empty, string.Empty);
            return ScheduledTaskOutcome.Success("已自动生成本周周报，可打开周报页检查和修改。");
        }
        catch (Exception exception) when (exception is DatabaseAccessException
            or InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            return ScheduledTaskOutcome.Failed($"自动生成周报失败：{exception.Message}");
        }
    }
}
