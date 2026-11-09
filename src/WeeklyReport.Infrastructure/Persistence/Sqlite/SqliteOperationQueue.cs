// [新增-离线存储] Phase 1 切片 D：SQLite 操作队列——回调式会话（ADR-03 §4.2）。
// RunInOperationAsync：创建客户端 → 开启 Serializable 事务 → 业务回调（经 session.Database 执行业务写入，
// 与已验证的 SqlSugarTransactions.SerializableAsync 内联模式同构）→ 操作组入队（同一事务）→ 提交；
// 回调或入队抛错则整体回滚（业务写入与队列行同生共死）。
// [新增-SQLite单机] 计划《SQLite单机存储改造计划》阶段 2：从本地业务写事务中分离远端 outbox——
// 事务与回滚语义保留（业务写入仍原子）；生产路径不注册入队开关，不向 sync_operation 插入新行，
// 历史队列原样保留。入队能力经 SetEnqueueEnabled 按库路径显式开启：仅供保留同步机械的测试覆盖
// 与未来重新启用同步时承接（见类内注释）。
// 设计勘误：第一版 AsyncLocal 环境作用域因方向性缺陷与 SqlSugar 跨上下文事务客户端挂死而废弃；
// 此处 AsyncLocal 仅用于“向下流动”的嵌套防护（同一异步流内嵌套开启会被拒绝）。
// Application 层队列接口待“有效视图接入”切片随服务改造定稿（组合层先行）。
using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SqlSugar;
using WeeklyReport.Application;

namespace WeeklyReport.Infrastructure.Persistence.Sqlite;

/// <summary>操作组会话：业务回调经 Database 在组事务内执行写入；Record 累积语义化变更。</summary>
public sealed class LocalOperationSession
{
    private readonly List<LocalOperationChange> _changes = [];

    internal LocalOperationSession(Guid groupId, SqlSugarClient database)
    {
        GroupId = groupId;
        Database = database;
    }

    public Guid GroupId { get; }

    /// <summary>组事务客户端：仅限 RunInOperationAsync 回调内使用（组合层业务写入）。</summary>
    public SqlSugarClient Database { get; }

    internal bool HasChanges => _changes.Count > 0;

    public void Record(LocalOperationChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        _changes.Add(change);
    }

    internal string BuildChangesJson() => JsonSerializer.Serialize(_changes);
}

public sealed class SqliteOperationQueue(string databasePath)
{
    private static readonly AsyncLocal<bool> InOperation = new();

    // [新增-SQLite单机] 计划阶段 2：按库路径的入队开关（默认关闭）。生产路径从不注册——业务写事务
    // 不新增任何待同步记录；保留的同步闭环测试（Applier/Coordinator/Safety/镜像拒绝）按各自临时库路径
    // 注册后，仓储写入与旧版一致自动入队，继续覆盖"保留但停用"的同步代码（未来重新启用同步的承接点）。
    // 按路径而非全局静态布尔：测试类并行时各持唯一路径，互不干扰。
    private static readonly ConcurrentDictionary<string, bool> EnqueueEnabledPaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>注册/注销某库路径的自动入队（仅供同步机械测试与未来重启用；生产代码不得调用）。</summary>
    public static void SetEnqueueEnabled(string path, bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (enabled)
        {
            EnqueueEnabledPaths[Path.GetFullPath(path)] = true;
        }
        else
        {
            EnqueueEnabledPaths.TryRemove(Path.GetFullPath(path), out _);
        }
    }

    internal static bool IsEnqueueEnabled(string path) =>
        EnqueueEnabledPaths.TryGetValue(Path.GetFullPath(path), out var enabled) && enabled;

    public async Task<T> RunInOperationAsync<T>(
        string operationKind,
        Func<LocalOperationSession, Task<T>> business,
        string? baselineRef = null,
        Guid? dependsOn = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKind);
        ArgumentNullException.ThrowIfNull(business);
        if (InOperation.Value)
        {
            throw new InvalidOperationException("已存在进行中的本地操作组，禁止嵌套开启。");
        }

        await SqliteDatabase.EnsureInitializedAsync(databasePath);
        cancellationToken.ThrowIfCancellationRequested();

        InOperation.Value = true;
        try
        {
            using var client = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = SqliteDatabase.BuildConnectionString(databasePath),
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = true
            });
            var session = new LocalOperationSession(Guid.NewGuid(), client);
            try
            {
                await client.Ado.BeginTranAsync(System.Data.IsolationLevel.Serializable);
                var result = await InvokeBusinessAsync(business, session);

                // [旧代码-SQLite单机] 原无条件 outbox 入队（业务变更与 sync_operation 行同事务写入）。
                // [新增-SQLite单机] 计划阶段 2：outbox 解耦——入队仅对显式注册的库路径发生（同步机械测试/
                // 未来重启用）；生产路径不注册，业务写入与队列彻底分离、不新增待同步记录。
                if (IsEnqueueEnabled(databasePath) && session.HasChanges)
                {
                    var changesJson = session.BuildChangesJson();
                    var contentHash = SqliteOperationPayload.ComputeHash(changesJson);
                    var inserted = await client.Ado.ExecuteCommandAsync(
                        "INSERT INTO sync_operation (op_group_id, operation_kind, changes_json, payload_version, " +
                        "baseline_ref, depends_on, " +
                        "status, content_hash, created_at_utc, attempted_at_utc) " +
                        "VALUES (@opGroupId, @operationKind, @changesJson, @payloadVersion, @baselineRef, @dependsOn, " +
                        "@status, @contentHash, @createdAt, NULL)",
                        new
                        {
                            opGroupId = session.GroupId.ToString("D"),
                            operationKind,
                            changesJson,
                            payloadVersion = SqliteOperationPayload.CurrentVersion,
                            baselineRef,
                            dependsOn = dependsOn?.ToString("D"),
                            status = LocalSyncOperationStatus.Pending.ToString(),
                            contentHash,
                            createdAt = SqliteTimestamp.FormatUtcNow()
                        });
                    if (inserted != 1)
                    {
                        throw new InvalidOperationException("本地操作组入队失败。");
                    }
                }

                await client.Ado.CommitTranAsync();
                return result;
            }
            catch (Exception exception)
            {
                await SafeRollbackAsync(client);
                if (exception is SqliteException or SqlSugarException)
                {
                    throw new DatabaseAccessException(
                        "本地存储操作失败，请检查 SQLite 文件与磁盘状态。", exception);
                }

                throw;
            }
        }
        finally
        {
            InOperation.Value = false;
        }
    }

    // [新增-离线存储] Phase 1 切片 F：业务回调统一异常包装（与 SqliteExecutor 口径一致，
    // 唯一约束冲突等 SQLite 原生异常在仓储边界即转为 DatabaseAccessException）
    private static async Task<T> InvokeBusinessAsync<T>(
        Func<LocalOperationSession, Task<T>> business, LocalOperationSession session)
    {
        try
        {
            return await business(session);
        }
        catch (Microsoft.Data.Sqlite.SqliteException exception)
        {
            throw new DatabaseAccessException("本地存储操作失败，请检查 SQLite 文件与磁盘状态。", exception);
        }
    }

    private static async Task SafeRollbackAsync(SqlSugarClient client)
    {
        try
        {
            await client.Ado.RollbackTranAsync();
        }
        catch
        {
            // 连接已断等场景：客户端随后释放，事务由 SQLite 自行回滚
        }
    }
}
