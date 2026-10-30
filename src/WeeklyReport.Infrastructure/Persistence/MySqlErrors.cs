using MySqlConnector;
using WeeklyReport.Application;

namespace WeeklyReport.Infrastructure.Persistence;

internal static class MySqlErrors
{
    public static DatabaseAccessException Wrap(MySqlException exception)
    {
        var message = exception.Number switch
        {
            1044 or 1045 => "MySQL 认证失败或账号权限不足，请检查账号和数据库权限。",
            1049 => "指定的 MySQL 数据库不存在，请先创建或选择已有数据库。",
            1062 => "数据已存在，请刷新后再试。",
            1451 => "项目仍有关联工作项，不能直接删除。",
            2002 or 2003 => "无法连接本机 MySQL，请检查服务和端口。",
            _ => "MySQL 操作失败，请检查数据库连接、权限和服务状态。"
        };
        return new DatabaseAccessException(message, exception);
    }
}
