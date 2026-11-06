using System.Data;
using SqlSugar;
using WeeklyReport.Application;

namespace WeeklyReport.Infrastructure.Persistence;

internal static class SqlSugarTransactions
{
    public static async Task<T> SerializableAsync<T>(SqlSugarClient database, Func<Task<T>> action)
    {
        await database.Ado.BeginTranAsync(IsolationLevel.Serializable);
        try
        {
            var result = await action();
            await database.Ado.CommitTranAsync();
            return result;
        }
        catch (Exception exception)
        {
            try
            {
                await database.Ado.RollbackTranAsync();
            }
            catch (Exception rollbackException)
            {
                throw new DatabaseAccessException("数据库操作失败且事务回滚失败，请检查数据库。",
                    new AggregateException(exception, rollbackException));
            }

            throw;
        }
    }
}
