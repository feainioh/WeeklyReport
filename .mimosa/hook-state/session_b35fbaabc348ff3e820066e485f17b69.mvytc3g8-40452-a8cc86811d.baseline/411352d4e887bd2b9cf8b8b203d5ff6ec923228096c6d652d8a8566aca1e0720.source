using WeeklyReport.Application;
using WeeklyReport.Domain;

namespace WeeklyReport.UnitTests;

public class WorkEntryServiceTests
{
    [Fact]
    public async Task Create_ValidatesAndPersistsTrimmedContent()
    {
        var repository = new MemoryRepository();
        var clock = new TestClock(new DateTimeOffset(2026, 10, 30, 8, 0, 0, TimeSpan.Zero));
        var service = new WorkEntryService(repository, clock);

        var entry = await service.CreateAsync(new DateOnly(2026, 10, 30), "  完成测试  ");

        Assert.Equal("完成测试", entry.Content);
        Assert.Equal(clock.GetUtcNow(), entry.CreatedAtUtc);
        Assert.Same(entry, await repository.FindAsync(entry.Id));
    }

    [Fact]
    public async Task Create_RejectsEmptyContentWithoutSaving()
    {
        var repository = new MemoryRepository();
        var service = new WorkEntryService(repository, TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new DateOnly(2026, 10, 30), "  "));
        Assert.Empty(await repository.GetByWeekAsync(WeekPeriod.FromDate(new DateOnly(2026, 10, 30))));
    }

    [Fact]
    public async Task Update_PreservesIdentityAndCreationTime()
    {
        var repository = new MemoryRepository();
        var clock = new TestClock(new DateTimeOffset(2026, 10, 30, 8, 0, 0, TimeSpan.Zero));
        var service = new WorkEntryService(repository, clock);
        var original = await service.CreateAsync(new DateOnly(2026, 10, 30), "原内容");
        clock.Now = clock.Now.AddHours(1);

        var updated = await service.UpdateAsync(original.Id, new DateOnly(2026, 10, 31), "新内容", 90);

        Assert.Equal(original.Id, updated.Id);
        Assert.Equal(original.CreatedAtUtc, updated.CreatedAtUtc);
        Assert.Equal(clock.Now, updated.UpdatedAtUtc);
        Assert.Equal("新内容", updated.Content);
        Assert.Equal(new DateOnly(2026, 10, 31), updated.WorkDate);
        Assert.Equal(90, updated.ConfirmedMinutes);
    }

    [Fact]
    public async Task Update_RejectsClockMovingBackwards()
    {
        var repository = new MemoryRepository();
        var clock = new TestClock(new DateTimeOffset(2026, 10, 30, 8, 0, 0, TimeSpan.Zero));
        var service = new WorkEntryService(repository, clock);
        var original = await service.CreateAsync(new DateOnly(2026, 10, 30), "原内容");
        clock.Now = clock.Now.AddMinutes(-1);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UpdateAsync(original.Id, original.WorkDate, "新内容", null));
        Assert.Equal("原内容", (await repository.FindAsync(original.Id))!.Content);
    }

    [Fact]
    public async Task Delete_ReportsMissingRecords()
    {
        var service = new WorkEntryService(new MemoryRepository(), TimeProvider.System);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.DeleteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Update_AdvancesTimestampWhenClockHasNotChanged()
    {
        var repository = new MemoryRepository();
        var clock = new TestClock(new DateTimeOffset(2026, 10, 30, 8, 0, 0, TimeSpan.Zero));
        var service = new WorkEntryService(repository, clock);
        var original = await service.CreateAsync(new DateOnly(2026, 10, 30), "原内容");

        var updated = await service.UpdateAsync(original.Id, original.WorkDate, "新内容", null);

        Assert.Equal(original.UpdatedAtUtc.AddTicks(10), updated.UpdatedAtUtc);
    }

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class MemoryRepository : IWorkEntryRepository
    {
        private readonly Dictionary<Guid, WorkEntry> _entries = new();

        public Task<IReadOnlyList<WorkEntry>> GetByWeekAsync(WeekPeriod week) =>
            Task.FromResult<IReadOnlyList<WorkEntry>>(_entries.Values.Where(entry => week.Contains(entry.WorkDate)).ToArray());

        public Task<WorkEntry?> FindAsync(Guid id) =>
            Task.FromResult(_entries.GetValueOrDefault(id));

        public Task AddAsync(WorkEntry entry)
        {
            _entries.Add(entry.Id, entry);
            return Task.CompletedTask;
        }

        public Task<bool> UpdateAsync(WorkEntry entry)
        {
            if (!_entries.ContainsKey(entry.Id))
            {
                return Task.FromResult(false);
            }

            _entries[entry.Id] = entry;
            return Task.FromResult(true);
        }

        public Task<bool> DeleteAsync(Guid id) => Task.FromResult(_entries.Remove(id));
    }
}
