// [新增-离线存储] Phase 1 切片 A：SQLite 版 SqlSugar 执行器——异常统一包装为 DatabaseAccessException（与 MySQL 侧口径一致）。
// 切片 D 勘误：曾尝试 AsyncLocal 环境作用域让仓储隐式加入操作组事务，实测有两个不可修复的问题——
// (1) AsyncLocal 在 async 方法内设值不会向上传播到调用方上下文（仓储永远看不到环境值）；
// (2) SqlSugar 事务客户端跨创建上下文复用会挂死/原生崩溃。
// 操作组事务改为 SqliteOperationQueue 的回调式会话（RunInOperationAsync），与已验证的
// SqlSugarTransactions.SerializableAsync 内联模式同构；本执行器保持单一职责。
using Microsoft.Data.Sqlite;
using SqlSugar;
using WeeklyReport.Application;

namespace WeeklyReport.Infrastructure.Persistence.Sqlite;

internal sealed class SqliteExecutor(string databasePath)
{
    // [旧代码-离线存储] 切片 D 第一版环境作用域（已废弃，原因见文件头注释）
    // internal static readonly AsyncLocal<(SqlSugarClient? Client, string? DatabasePath)> AmbientOperation = new();

    public async Task<T> ExecuteAsync<T>(Func<SqlSugarClient, Task<T>> action)
    {
        // [旧代码-离线存储] 切片 D 第一版：环境事务内执行分支（随 AsyncLocal 方向性问题一并废弃）
        // var ambient = AmbientOperation.Value;
        // if (ambient.Client is not null) { ... return await action(ambient.Client); }

        try
        {
            using var database = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = SqliteDatabase.BuildConnectionString(databasePath),
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = true
            });
            return await action(database);
        }
        catch (SqliteException exception)
        {
            throw new DatabaseAccessException("本地存储操作失败，请检查 SQLite 文件与磁盘状态。", exception);
        }
        catch (SqlSugarException exception)
        {
            throw new DatabaseAccessException("本地存储操作失败，请检查 SQLite 文件与磁盘状态。", exception);
        }
    }
}
