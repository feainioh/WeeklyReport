using MySqlConnector;
using SqlSugar;
using WeeklyReport.Application;

namespace WeeklyReport.Infrastructure.Persistence;

internal sealed class SqlSugarExecutor(IDatabaseConfigurationStore configurationStore)
{
    public async Task<T> ExecuteAsync<T>(Func<SqlSugarClient, Task<T>> action)
    {
        try
        {
            var settings = await configurationStore.LoadAsync()
                ?? throw new InvalidOperationException("尚未配置本地 MySQL，请先打开设置。");
            using var database = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = MySqlConnectionStrings.Build(settings),
                DbType = DbType.MySql,
                IsAutoCloseConnection = true
            });
            return await action(database);
        }
        catch (MySqlException exception)
        {
            throw MySqlErrors.Wrap(exception);
        }
        catch (SqlSugarException exception) when (exception.InnerException is MySqlException mysqlException)
        {
            throw MySqlErrors.Wrap(mysqlException);
        }
        catch (SqlSugarException exception)
        {
            throw new DatabaseAccessException("数据库操作失败，请检查本机 MySQL 连接、权限和表结构。", exception);
        }
    }
}
