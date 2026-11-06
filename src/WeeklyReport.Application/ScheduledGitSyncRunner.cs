using System.IO;

namespace WeeklyReport.Application;

public sealed class ScheduledGitSyncRunner(
    IGitProjectRepository projects,
    GitSyncService sync) : IScheduledTaskRunner
{
    public async Task<ScheduledTaskOutcome> RunAsync(
        ScheduledTaskKind kind,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        if (kind != ScheduledTaskKind.GitDailyItems)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Git 执行器不支持该任务类型。");
        }

        try
        {
            var configured = await projects.ListAsync();
            var bound = configured.Where(project => project.IsBound).ToArray();
            if (bound.Length == 0)
            {
                return ScheduledTaskOutcome.Skipped("没有已绑定本地仓库的 Git 项目。");
            }

            var prepared = await sync.PrepareAsync(
                bound, date, date, TimeZoneInfo.Local, cancellationToken);
            if (prepared.Statuses.Any(status => status.IsShallow))
            {
                return ScheduledTaskOutcome.Skipped("仓库包含浅克隆，未自动应用；请手动同步并确认历史完整性。");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var applied = await sync.ApplyAsync(
                prepared, userConfirmed: true, skipDeletedConflicts: true, acceptShallowHistory: false);
            var changed = applied.Created + applied.Updated;
            return changed == 0
                ? ScheduledTaskOutcome.NoChanges("今日 Git 工作项无需更新。")
                : ScheduledTaskOutcome.Success($"已同步今日 {changed} 条工作项。", changed);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return ScheduledTaskOutcome.Failed($"Git 自动同步失败：{exception.Message}");
        }
    }

    private static bool IsExpected(Exception exception) =>
        exception is GitReadException or DatabaseAccessException or InvalidOperationException
            or ArgumentException or IOException or UnauthorizedAccessException or OperationCanceledException;
}
