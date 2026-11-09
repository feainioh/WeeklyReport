// [新增-SQLite单机] 《SQLite单机存储改造计划》阶段 1：SQLite-only 启动路由决策器（计划 §3.1 三情形区分）。
// 只做判定不做 UI：区分「已有 SQLite 工作库 / 只有 MySQL 数据 / 新安装」，
// 并识别损坏库、未初始化库与 MysqlOnly（曾显式停用本地优先）三类不可静默放行的状态。
// 判定依据仅本机文件与元数据（读 database.json 判断 MySQL 是否配置过，不连接 MySQL）；
// 不创建空库掩盖损坏或迁移失败（计划 §1/§3.1），空库创建必须由调用方在用户明确选择后执行。
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text.Json;
using WeeklyReport.Application;
using WeeklyReport.Infrastructure.Persistence.Sqlite;

namespace WeeklyReport.App.Services;

public enum SqliteOnlyStartupDecisionKind
{
    /// <summary>SQLite 工作库就绪（Ready/Suspended），可直接以本地库启动。</summary>
    UseExistingLibrary,

    /// <summary>全新安装（无 SQLite 库且从未配置 MySQL），允许创建空库。</summary>
    CreateEmptyLibrary,

    /// <summary>无 SQLite 库但配置过 MySQL——只有 MySQL 数据的用户，须由用户明确选择（迁移或建空库）。</summary>
    MysqlDataWithoutLocalLibrary,

    /// <summary>SQLite 文件存在但损坏/不可读写——明确报错，不回退 MySQL、不覆盖原库。</summary>
    CorruptedLibrary,

    /// <summary>SQLite 文件存在但未初始化（可能迁移中断）——报错指引，禁止静默建空库。</summary>
    UninitializedLibrary,

    /// <summary>曾显式停用本地优先（MysqlOnly）——权威数据在 MySQL，须回旧版迁移，不推断数据来源。</summary>
    DisabledLibrary
}

public sealed record SqliteOnlyStartupDecision(
    SqliteOnlyStartupDecisionKind Kind,
    LocalStorageState? State = null,
    string? Detail = null)
{
    /// <summary>是否可以继续启动（UseExistingLibrary 由调用方直接使用；CreateEmptyLibrary/MysqlData 由调用方按用户选择处理）。</summary>
    public bool IsBlocked => Kind is SqliteOnlyStartupDecisionKind.CorruptedLibrary
        or SqliteOnlyStartupDecisionKind.UninitializedLibrary
        or SqliteOnlyStartupDecisionKind.DisabledLibrary;
}

public sealed class SqliteOnlyStorageGate(
    string databasePath,
    IDatabaseConfigurationStore configurationStore)
{
    public async Task<SqliteOnlyStartupDecision> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(databasePath))
        {
            // 不能仅凭缺少 SQLite 文件认定新用户：配置过 MySQL 说明数据可能在 MySQL 侧（计划 §3.1）
            bool mysqlConfigured;
            try
            {
                mysqlConfigured = await configurationStore.LoadAsync(cancellationToken) is not null;
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException
                or UnauthorizedAccessException or SecurityException or CryptographicException or JsonException)
            {
                return new SqliteOnlyStartupDecision(
                    SqliteOnlyStartupDecisionKind.MysqlDataWithoutLocalLibrary,
                    Detail: $"无法读取数据库配置以判断数据来源：{exception.GetType().Name}");
            }

            return mysqlConfigured
                ? new SqliteOnlyStartupDecision(SqliteOnlyStartupDecisionKind.MysqlDataWithoutLocalLibrary)
                : new SqliteOnlyStartupDecision(SqliteOnlyStartupDecisionKind.CreateEmptyLibrary);
        }

        LocalStorageState state;
        try
        {
            state = await new SqliteLocalStore(databasePath).GetStateAsync(cancellationToken);
        }
        catch (DatabaseAccessException exception)
        {
            // 损坏/不可读写：明确报错，不回退 MySQL，也不覆盖原库（计划 §1）
            return new SqliteOnlyStartupDecision(
                SqliteOnlyStartupDecisionKind.CorruptedLibrary, Detail: exception.GetType().Name);
        }

        return state.Status switch
        {
            LocalStorageStatus.Ready or LocalStorageStatus.Suspended
                => new SqliteOnlyStartupDecision(SqliteOnlyStartupDecisionKind.UseExistingLibrary, state),
            // MysqlOnly 是用户显式停用本地优先的结果，权威数据在 MySQL；
            // SQLite-only 版本无法验证 MySQL 数据，不自动覆盖、合并或推断哪个更新（计划 §3.1）
            LocalStorageStatus.MysqlOnly
                => new SqliteOnlyStartupDecision(SqliteOnlyStartupDecisionKind.DisabledLibrary, state),
            _ => new SqliteOnlyStartupDecision(SqliteOnlyStartupDecisionKind.UninitializedLibrary, state)
        };
    }
}
