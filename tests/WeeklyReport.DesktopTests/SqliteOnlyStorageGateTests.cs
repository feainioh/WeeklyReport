// [新增-SQLite单机] 《SQLite单机存储改造计划》阶段 1：SQLite-only 启动路由决策器测试——
// 区分已有工作库/只有 MySQL 数据/新安装三种情形（计划 §3.1），并覆盖损坏库、未初始化库、
// MysqlOnly（曾停用本地优先）三类必须明确报错的状态。
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using WeeklyReport.App.Services;
using WeeklyReport.Application;
using WeeklyReport.Infrastructure.Persistence.Sqlite;

namespace WeeklyReport.DesktopTests;

public sealed class SqliteOnlyStorageGateTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "WeeklyReport.GateTests", $"gate-{Guid.NewGuid():N}");

    private string DatabasePath => Path.Combine(_directory, "work.db");

    private SqliteOnlyStorageGate GateWith(bool mysqlConfigured) =>
        new(DatabasePath, new StubConfigurationStore(mysqlConfigured));

    public SqliteOnlyStorageGateTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public async Task MissingLibraryAndNoMysqlConfig_NewInstall_CreatesEmptyLibrary()
    {
        var decision = await GateWith(mysqlConfigured: false).EvaluateAsync();

        Assert.Equal(SqliteOnlyStartupDecisionKind.CreateEmptyLibrary, decision.Kind);
        Assert.False(decision.IsBlocked);
    }

    [Fact]
    public async Task MissingLibraryWithMysqlConfig_RequiresUserChoice()
    {
        // 不能仅凭缺少 SQLite 文件认定新用户（计划 §3.1）
        var decision = await GateWith(mysqlConfigured: true).EvaluateAsync();

        Assert.Equal(SqliteOnlyStartupDecisionKind.MysqlDataWithoutLocalLibrary, decision.Kind);
        Assert.False(decision.IsBlocked);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("crypto")]
    [InlineData("permission")]
    public async Task MissingLibraryWithUnreadableConfiguration_RequiresUserChoice(string error)
    {
        Exception exception = error switch
        {
            "json" => new JsonException(),
            "crypto" => new CryptographicException(),
            _ => new UnauthorizedAccessException()
        };
        var decision = await new SqliteOnlyStorageGate(DatabasePath, new FailingConfigurationStore(exception))
            .EvaluateAsync();

        Assert.Equal(SqliteOnlyStartupDecisionKind.MysqlDataWithoutLocalLibrary, decision.Kind);
        Assert.Contains(exception.GetType().Name, decision.Detail);
        Assert.False(File.Exists(DatabasePath));
    }

    [Fact]
    public async Task ReadyLibrary_StartsWithLocalData()
    {
        await new SqliteLocalStore(DatabasePath).StartEmptyWorkspaceAsync();

        var decision = await GateWith(mysqlConfigured: true).EvaluateAsync();

        Assert.Equal(SqliteOnlyStartupDecisionKind.UseExistingLibrary, decision.Kind);
        Assert.NotNull(decision.State);
        Assert.Equal(LocalStorageStatus.Ready, decision.State!.Status);
    }

    [Fact]
    public async Task SuspendedLibrary_StillUsableLocally()
    {
        var store = new SqliteLocalStore(DatabasePath);
        await store.StartEmptyWorkspaceAsync();
        await store.SuspendSyncAsync("身份校验失败（历史遗留）");

        var decision = await GateWith(mysqlConfigured: true).EvaluateAsync();

        Assert.Equal(SqliteOnlyStartupDecisionKind.UseExistingLibrary, decision.Kind);
        Assert.Equal(LocalStorageStatus.Suspended, decision.State!.Status);
    }

    [Fact]
    public async Task CorruptedLibrary_BlocksStartup()
    {
        File.WriteAllBytes(DatabasePath, "not a sqlite database at all"u8.ToArray());

        var decision = await GateWith(mysqlConfigured: false).EvaluateAsync();

        Assert.Equal(SqliteOnlyStartupDecisionKind.CorruptedLibrary, decision.Kind);
        Assert.True(decision.IsBlocked);
    }

    [Fact]
    public async Task UninitializedLibrary_BlocksStartup()
    {
        // 表结构已建但 local_meta 无行（迁移中断形态）
        await SqliteDatabase.EnsureInitializedAsync(DatabasePath);

        var decision = await GateWith(mysqlConfigured: false).EvaluateAsync();

        Assert.Equal(SqliteOnlyStartupDecisionKind.UninitializedLibrary, decision.Kind);
        Assert.True(decision.IsBlocked);
    }

    [Fact]
    public async Task MysqlOnlyLibrary_BlocksStartup()
    {
        var store = new SqliteLocalStore(DatabasePath);
        await store.StartEmptyWorkspaceAsync();
        await store.DisableLocalFirstAsync();

        var decision = await GateWith(mysqlConfigured: true).EvaluateAsync();

        Assert.Equal(SqliteOnlyStartupDecisionKind.DisabledLibrary, decision.Kind);
        Assert.True(decision.IsBlocked);
    }

    private sealed class StubConfigurationStore(bool configured) : IDatabaseConfigurationStore
    {
        public Task<DatabaseConnectionSettings?> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(configured
                ? new DatabaseConnectionSettings("127.0.0.1", 3306, "weekly_report", "root", "password")
                : null);

        public Task SaveAsync(DatabaseConnectionSettings settings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("测试桩不写入配置。");
    }

    private sealed class FailingConfigurationStore(Exception error) : IDatabaseConfigurationStore
    {
        public Task<DatabaseConnectionSettings?> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<DatabaseConnectionSettings?>(error);

        public Task SaveAsync(DatabaseConnectionSettings settings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
