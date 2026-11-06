// [新增-备份离线化] IBackupStore 共享契约基类（切片 1）：MySQL 与未来 SQLite 实现跑同一组行为测试。
// 契约范围：捕获一致性、导入新增/跳过/覆盖计数、基线变化拒绝导入、失败整体回滚、字段完整往返；
// 绑定保留等仓储特有断言留在各数据库专属测试中，不进入契约。
// 基类方法不带 [Fact]：跳过策略因库而异（MySQL 走 MySqlIntegrationFact 环境门禁，SQLite 本地常开），
// 由子类提供带各自 Fact 特性的薄包装方法。
using WeeklyReport.Application;
using WeeklyReport.Domain;

namespace WeeklyReport.IntegrationTests;

public abstract class BackupStoreContract
{
    protected abstract Task<BackupService> CreateServiceAsync();

    /// <summary>预览后修改某手工记录内容（用于基线变化注入），实现须推进更新时间。</summary>
    protected abstract Task TouchManualEntryAsync(Guid id, string newContent);

    /// <summary>注入“周报插入失败”故障（MySQL 用触发器；SQLite 实现可用等价触发器）。</summary>
    protected abstract Task InjectReportInsertFailureAsync(Guid reportId);

    protected abstract Task RemoveReportInsertFailureAsync();

    protected abstract Task CleanupAsync(Guid projectId, Guid manualEntryId, Guid reportId);

    protected virtual async Task Import_SkipOverwriteAndStalePreviewAsync()
    {
        var service = await CreateServiceAsync();
        var source = CreateBackup(FindUnusedWeek(await service.CaptureAsync()));
        var projectId = source.Projects[0].Id;
        var manualId = source.Entries.Single(entry => entry.Kind == BackupEntryKind.Manual).Id;
        var reportId = source.Reports[0].Id;

        try
        {
            // 确认门禁：未经用户确认必须拒绝
            var first = await service.PrepareImportAsync(source);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.ApplyImportAsync(first, BackupConflictPolicy.Skip, false));

            // 首次导入全部新增；重复导入按冲突全部跳过
            Assert.Equal(6, (await service.ApplyImportAsync(first, BackupConflictPolicy.Skip, true)).Added);
            Assert.Equal(6, (await service.ApplyImportAsync(
                await service.PrepareImportAsync(source), BackupConflictPolicy.Skip, true)).Skipped);

            // 覆盖策略：业务对象覆盖、提交来源重新应用
            var changed = source with
            {
                Projects = [source.Projects[0] with { Name = "契约覆盖项目" }],
                Entries = source.Entries.Select(entry => entry.Kind == BackupEntryKind.Manual
                    ? entry with { Description = "契约覆盖记录", ConfirmedMinutes = 90 }
                    : entry with { Description = "契约覆盖工作项", ConfirmedMinutes = 60 }).ToArray(),
                Reports = [source.Reports[0] with { WorkSummary = "契约覆盖周报" }]
            };
            var overwrite = await service.ApplyImportAsync(
                await service.PrepareImportAsync(changed), BackupConflictPolicy.Overwrite, true);
            Assert.Equal(4, overwrite.Overwritten);
            Assert.Equal(2, overwrite.Added);

            // 字段完整往返：覆盖结果经捕获可读，工时数值不丢失
            var captured = await service.CaptureAsync();
            var manual = captured.Entries.Single(entry => entry.Id == manualId);
            Assert.Equal("契约覆盖记录", manual.Description);
            Assert.Equal(90, manual.ConfirmedMinutes);
            Assert.Equal("契约覆盖周报", captured.Reports.Single(report => report.Id == reportId).WorkSummary);

            // 基线比较：预览后数据变化必须拒绝导入且不修改数据
            var stale = await service.PrepareImportAsync(changed);
            await TouchManualEntryAsync(manualId, "预览后的修改");
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.ApplyImportAsync(stale, BackupConflictPolicy.Overwrite, true));
            Assert.Equal("预览后的修改",
                (await service.CaptureAsync()).Entries.Single(entry => entry.Id == manualId).Description);
        }
        finally
        {
            await CleanupAsync(projectId, manualId, reportId);
        }
    }

    protected virtual async Task Import_FailureMidwayRollsBackAsync()
    {
        var service = await CreateServiceAsync();
        var source = CreateBackup(FindUnusedWeek(await service.CaptureAsync()));
        var projectId = source.Projects[0].Id;
        var manualId = source.Entries.Single(entry => entry.Kind == BackupEntryKind.Manual).Id;
        var reportId = source.Reports[0].Id;

        try
        {
            var plan = await service.PrepareImportAsync(source);
            await InjectReportInsertFailureAsync(reportId);
            try
            {
                // 失败必须包装为应用层标准异常类型（SQLite 实现同样要求）
                await Assert.ThrowsAsync<DatabaseAccessException>(
                    () => service.ApplyImportAsync(plan, BackupConflictPolicy.Skip, true));
            }
            finally
            {
                await RemoveReportInsertFailureAsync();
            }

            // 中途失败零部分写入
            var after = await service.CaptureAsync();
            Assert.DoesNotContain(after.Projects, project => project.Id == projectId);
            Assert.DoesNotContain(after.Entries, entry => entry.Id == manualId);
            Assert.DoesNotContain(after.Reports, report => report.Id == reportId);
        }
        finally
        {
            await CleanupAsync(projectId, manualId, reportId);
        }
    }

    protected virtual async Task Capture_IsStableAcrossCallsAsync()
    {
        var service = await CreateServiceAsync();
        var first = await service.CaptureAsync();
        var second = await service.CaptureAsync();

        // 连续捕获等价（不含导出时间戳）：捕获必须产生一致性快照，不产生重复或漂移
        Assert.Equal(first.Projects.OrderBy(item => item.Id), second.Projects.OrderBy(item => item.Id));
        Assert.Equal(first.Entries.OrderBy(item => item.Id), second.Entries.OrderBy(item => item.Id));
        Assert.Equal(first.Commits.OrderBy(item => item.Hash).ThenBy(item => item.ProjectId),
            second.Commits.OrderBy(item => item.Hash).ThenBy(item => item.ProjectId));
        Assert.Equal(first.Reports.OrderBy(item => item.Id), second.Reports.OrderBy(item => item.Id));
    }

    protected static BackupDocument CreateBackup(DateOnly week)
    {
        var now = StorageDateRange.NormalizeUtcTimestamp(DateTimeOffset.UtcNow);
        var project = Guid.NewGuid();
        var manual = Guid.NewGuid();
        var git = Guid.NewGuid();
        var atNine = new DateTimeOffset(week.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero);
        return new BackupDocument(
            1, now,
            [new BackupProject(project, "契约项目")],
            [
                new BackupEntry(manual, BackupEntryKind.Manual, null, week, "契约手工记录", null, 75, false, now, now),
                new BackupEntry(git, BackupEntryKind.Git, project, week, "契约 Git 工作项", 60, 45, false, null, null)
            ],
            [
                new BackupCommit(project, git, new string('a', 40), "name", "test@example.invalid", atNine, "one"),
                new BackupCommit(project, git, new string('b', 40), "name", "test@example.invalid", atNine.AddHours(1), "two")
            ],
            [new BackupReport(Guid.NewGuid(), week, week.AddDays(6), "本周", "下周", "风险", 120, 0, 0, now, now)]);
    }

    protected static DateOnly FindUnusedWeek(BackupDocument current)
    {
        var existing = current.Reports.Select(report => report.WeekStart).ToHashSet();
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var week = new DateOnly(2000, 1, 3).AddDays(
                System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 5000) * 7);
            if (!existing.Contains(week))
            {
                return week;
            }
        }

        throw new InvalidOperationException("无法在隔离测试库中找到空闲周。");
    }
}
