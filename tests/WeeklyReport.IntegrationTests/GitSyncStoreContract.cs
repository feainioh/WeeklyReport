// [新增-离线存储] Phase 1 切片 B：IGitSyncStore 共享契约——从 MySQL 专属测试提取的完整行为场景：
// 确认门禁、跨项目重叠分摊（45/45）、人工编辑与已确认工时保护、重复同步去重、基线过期拒绝、
// 软删除脱敏与重建冲突、重算不覆盖确认值、周查询过滤已删除。
// 库相关钩子（初始化/仓储构造/估算值破坏性修改/清理）由子类注入。
using WeeklyReport.Application;
using WeeklyReport.Domain;

namespace WeeklyReport.IntegrationTests;

public abstract class GitSyncStoreContract
{
    protected abstract Task InitializeAsync();

    protected abstract Task<IGitProjectRepository> CreateProjectsRepositoryAsync();

    protected abstract Task<IGitSyncStore> CreateSyncStoreAsync();

    /// <summary>绕过仓储直接改写估算值（模拟外部修改以触发重算场景）。</summary>
    protected abstract Task CorruptEstimateAsync(Guid id, int minutes);

    /// <summary>清理两个测试项目的全部相关行（git_commit_links/git_work_items/git_projects）。</summary>
    protected abstract Task CleanupProjectsAsync(Guid first, Guid second);

    protected virtual async Task Sync_IsTransactionalIdempotentAndPreservesManualEditsAsync()
    {
        await InitializeAsync();
        var projects = await CreateProjectsRepositoryAsync();
        var store = await CreateSyncStoreAsync();
        var projectA = new GitProject(Guid.NewGuid(), "项目 A", Path.GetTempPath(), GitAuthorField.Email, "test@example.invalid");
        var projectB = new GitProject(Guid.NewGuid(), "项目 B", Path.GetTempPath(), GitAuthorField.Email, "test@example.invalid");
        var day = await FindUnusedDayAsync(store);
        var reader = new TestGitReader();
        reader.Commits[projectA.Id] = [Commit(projectA.Id, 1, day, 9, 0), Commit(projectA.Id, 2, day, 10, 0)];
        reader.Commits[projectB.Id] = [Commit(projectB.Id, 3, day, 9, 30), Commit(projectB.Id, 4, day, 10, 30)];
        var service = new GitSyncService(reader, store, new GitSyncPlanner(new GitHourEstimator()));

        try
        {
            await projects.AddAsync(projectA);
            await projects.AddAsync(projectB);
            var first = await service.PrepareAsync([projectA, projectB], day, day, TimeZoneInfo.Utc);
            Assert.Equal(90, Assert.Single(first.Preview.SuggestedHours).TotalMinutes);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(first, false, false, false));
            Assert.DoesNotContain(await store.LoadAllAsync(),
                item => item.ProjectId == projectA.Id || item.ProjectId == projectB.Id);

            var applied = await service.ApplyAsync(first, true, false, false);
            Assert.Equal(2, applied.Created);
            var initial = await ProjectItemsAsync(store, projectA.Id, projectB.Id);
            var itemA = initial.Single(item => item.ProjectId == projectA.Id);
            var itemB = initial.Single(item => item.ProjectId == projectB.Id);
            Assert.Equal(45, itemA.EstimatedMinutes);
            Assert.Equal(45, itemB.EstimatedMinutes);
            Assert.Equal(2, itemA.Commits.Count);

            Assert.True(await store.UpdateWorkItemAsync(itemA.Id, "人工编辑", 90));
            reader.Commits[projectA.Id].Add(Commit(projectA.Id, 5, day, 11, 0));
            var second = await service.PrepareAsync([projectA], day, day, TimeZoneInfo.Utc);
            Assert.Equal(2, second.Preview.DuplicateCount);
            Assert.Equal("人工编辑", Assert.Single(second.Preview.Proposals).CurrentDescription);
            Assert.Equal(90, Assert.Single(second.Preview.Proposals).ConfirmedMinutes);
            var secondResult = await service.ApplyAsync(second, true, false, false);
            Assert.Equal(1, secondResult.Updated);

            var updated = await ProjectItemsAsync(store, projectA.Id, projectB.Id);
            itemA = updated.Single(item => item.ProjectId == projectA.Id);
            itemB = updated.Single(item => item.ProjectId == projectB.Id);
            Assert.Equal("人工编辑", itemA.Description);
            Assert.Equal(90, itemA.ConfirmedMinutes);
            Assert.Equal(90, itemA.EstimatedMinutes);
            Assert.Equal(30, itemB.EstimatedMinutes);
            Assert.Equal(3, itemA.Commits.Count);

            await CorruptEstimateAsync(itemB.Id, 999);
            Assert.Equal(1, await store.RecalculateDateAsync(day));
            Assert.Equal(30, (await ProjectItemsAsync(store, projectA.Id, projectB.Id))
                .Single(item => item.ProjectId == projectB.Id).EstimatedMinutes);

            var repeated = await service.PrepareAsync([projectA], day, day, TimeZoneInfo.Utc);
            Assert.Empty(repeated.Preview.Proposals);
            Assert.Equal(3, repeated.Preview.DuplicateCount);
            Assert.Equal(0, (await service.ApplyAsync(repeated, true, false, false)).Created);

            reader.Commits[projectA.Id].Add(Commit(projectA.Id, 6, day, 12, 0));
            var stale = await service.PrepareAsync([projectA], day, day, TimeZoneInfo.Utc);
            Assert.True(await store.UpdateWorkItemAsync(itemB.Id, "其他编辑", 20));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(stale, true, false, false));
            Assert.Equal(3, (await ProjectItemsAsync(store, projectA.Id, projectB.Id))
                .Single(item => item.ProjectId == projectA.Id).Commits.Count);

            Assert.True(await store.SoftDeleteWorkItemAsync(itemA.Id));
            var deletedItem = (await ProjectItemsAsync(store, projectA.Id, projectB.Id))
                .Single(item => item.ProjectId == projectA.Id);
            Assert.True(deletedItem.IsDeleted);
            Assert.Equal("（已删除）", deletedItem.Description);
            Assert.Null(deletedItem.ConfirmedMinutes);
            Assert.Null(deletedItem.EstimatedMinutes);
            Assert.All(deletedItem.Commits, commit =>
            {
                Assert.Equal(string.Empty, commit.AuthorName);
                Assert.Equal(string.Empty, commit.AuthorEmail);
                Assert.Equal(string.Empty, commit.Subject);
            });
            var deleted = await service.PrepareAsync([projectA], day, day, TimeZoneInfo.Utc);
            Assert.Single(deleted.Preview.DeletedConflicts);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(deleted, true, false, false));
            var skipped = await service.ApplyAsync(deleted, true, true, false);
            Assert.Equal(1, skipped.DeletedConflictCount);
            var weekItems = await store.GetByWeekAsync(WeekPeriod.FromDate(day));
            Assert.DoesNotContain(weekItems, item => item.Id == itemA.Id);
            Assert.Contains(weekItems, item =>
                item.Id == itemB.Id && item.ConfirmedMinutes == 20 && item.EstimatedMinutes == 60);
        }
        finally
        {
            await CleanupProjectsAsync(projectA.Id, projectB.Id);
        }
    }

    protected static async Task<IReadOnlyList<GitWorkItemSnapshot>> ProjectItemsAsync(
        IGitSyncStore store, Guid projectA, Guid projectB) =>
        (await store.LoadAllAsync()).Where(item => item.ProjectId == projectA || item.ProjectId == projectB).ToArray();

    protected static async Task<DateOnly> FindUnusedDayAsync(IGitSyncStore store)
    {
        var used = (await store.LoadAllAsync()).Select(item => item.WorkDate).ToHashSet();
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var day = new DateOnly(2000, 1, 1).AddDays(
                System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 30000));
            if (!used.Contains(day))
            {
                return day;
            }
        }

        throw new InvalidOperationException("无法在隔离测试库中找到空闲日期。");
    }

    protected static GitCommit Commit(Guid project, int value, DateOnly day, int hour, int minute) =>
        new(project, value.ToString("x").PadLeft(40, '0'), "Synthetic Author", "test@example.invalid",
            new DateTimeOffset(day.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.Zero), $"提交 {value}");

    protected sealed class TestGitReader : IGitCommitReader
    {
        public Dictionary<Guid, List<GitCommit>> Commits { get; } = new();

        public Task<GitReadResult> ReadAsync(GitRepositoryQuery query, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new GitReadResult(Commits[query.ProjectId].ToArray(), HasHead: true, IsShallow: false));
        }
    }
}
