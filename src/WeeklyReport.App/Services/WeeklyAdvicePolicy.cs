using WeeklyReport.Application;

namespace WeeklyReport.App.Services;

public static class WeeklyAdvicePolicy
{
    public static bool CanShow(
        SpriteBehaviorSettings settings,
        DateTimeOffset now,
        TimeSpan sinceLastInteraction,
        bool isVisible,
        bool hasPriorityBubble,
        bool isMuted,
        bool userInitiated)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!isVisible || hasPriorityBubble)
        {
            return false;
        }

        if (userInitiated)
        {
            return true;
        }

        var local = now.DateTime;
        return settings.DataAdvice
            && !isMuted
            && !SpriteMessagePolicy.IsQuietHours(TimeOnly.FromDateTime(local))
            && sinceLastInteraction >= TimeSpan.FromMinutes(5)
            && settings.LastAdviceDate != DateOnly.FromDateTime(local);
    }

    public static WeeklyAdvice Create(WeeklyProgressContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Storage is WeeklyStorageStatus.LocalUnreadable
            or WeeklyStorageStatus.MySqlNotConfigured
            or WeeklyStorageStatus.MySqlUnavailable
            or WeeklyStorageStatus.Unknown)
        {
            return new WeeklyAdvice(
                WeeklyAdviceKind.StorageUnavailable,
                "暂时无法读取本周状态，请检查存储设置。",
                WeeklyAdviceAction.StorageSettings,
                "打开存储设置");
        }

        if (context.ConflictCount > 0 || context.Storage == WeeklyStorageStatus.Conflict)
        {
            var text = context.ConflictCount > 0
                ? $"有 {context.ConflictCount} 项数据需要你确认，现有内容已保留。"
                : "有数据需要你确认，现有内容已保留。";
            return new WeeklyAdvice(
                WeeklyAdviceKind.ConflictPending,
                text,
                WeeklyAdviceAction.SyncStatus,
                "查看同步状态");
        }

        if (context.PendingSyncCount > 0 || context.Storage == WeeklyStorageStatus.SyncPending)
        {
            var text = context.PendingSyncCount > 0
                ? $"内容已保存在本机，还有 {context.PendingSyncCount} 项变更等待同步。"
                : "内容已保存在本机，仍有变更等待同步。";
            return new WeeklyAdvice(
                WeeklyAdviceKind.SyncPending,
                text,
                WeeklyAdviceAction.SyncStatus,
                "查看同步状态");
        }

        if (!context.IsBusinessDataComplete)
        {
            return new WeeklyAdvice(
                WeeklyAdviceKind.StorageUnavailable,
                "暂时无法完整读取本周状态，请检查存储设置。",
                WeeklyAdviceAction.StorageSettings,
                "打开存储设置");
        }

        if (context.UnknownHoursCount > 0)
        {
            return new WeeklyAdvice(
                WeeklyAdviceKind.UnknownHours,
                $"本周有 {context.UnknownHoursCount} 项工作还没有可用工时，可以检查一下。",
                WeeklyAdviceAction.GitWorkItems,
                "检查工作项");
        }

        if (context.TotalWorkItemCount == 0 && context.HasSavedReport == false)
        {
            return new WeeklyAdvice(
                WeeklyAdviceKind.NoWorkItems,
                "本周暂时没有工作记录，需要记一笔吗？",
                WeeklyAdviceAction.WorkEntries,
                "打开工作记录");
        }

        if (context.TotalWorkItemCount > 0 && context.HasSavedReport == false)
        {
            return new WeeklyAdvice(
                WeeklyAdviceKind.ReportNotSaved,
                "本周已有工作记录，周报还没保存，要整理一下吗？",
                WeeklyAdviceAction.WeeklyReport,
                "打开本周周报");
        }

        return new WeeklyAdvice(
            WeeklyAdviceKind.ReportSavedCheck,
            "本周周报已保存，可以打开检查是否需要补充。",
            WeeklyAdviceAction.WeeklyReport,
            "打开本周周报");
    }
}

public static class WeeklyProgressStatusFormatter
{
    public static string FormatStorage(WeeklyProgressContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var status = context.Storage switch
        {
            WeeklyStorageStatus.MySqlHealthy => "MySQL 正常",
            WeeklyStorageStatus.MySqlNotConfigured => "MySQL 未配置",
            WeeklyStorageStatus.MySqlUnavailable => "MySQL 暂不可用",
            WeeklyStorageStatus.LocalMode => "本地模式",
            WeeklyStorageStatus.SyncPending => "本地模式 · 待同步",
            WeeklyStorageStatus.Conflict => "本地模式 · 有待处理冲突",
            WeeklyStorageStatus.LocalUnreadable => "本地数据不可读",
            _ => "存储状态未知"
        };

        if (context.PendingSyncCount > 0 && context.Storage != WeeklyStorageStatus.SyncPending)
        {
            status += $" · 待同步 {context.PendingSyncCount}";
        }

        if (context.ConflictCount > 0 && context.Storage != WeeklyStorageStatus.Conflict)
        {
            status += $" · 冲突 {context.ConflictCount}";
        }

        if ((context.Storage is WeeklyStorageStatus.LocalMode
                or WeeklyStorageStatus.SyncPending
                or WeeklyStorageStatus.Conflict)
            && !string.IsNullOrWhiteSpace(context.Error))
        {
            status += " · 同步已暂停";
        }

        return status;
    }
}
