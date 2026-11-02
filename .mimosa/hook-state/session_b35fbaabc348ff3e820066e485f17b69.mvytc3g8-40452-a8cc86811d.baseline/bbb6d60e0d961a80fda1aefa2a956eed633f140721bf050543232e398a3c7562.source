using MySqlConnector;
using WeeklyReport.Application;

namespace WeeklyReport.Infrastructure.Persistence;

internal static class MySqlConnectionStrings
{
    public static string Build(DatabaseConnectionSettings settings)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = settings.Host,
            Port = (uint)settings.Port,
            Database = settings.Database,
            UserID = settings.UserName,
            Password = settings.Password,
            ConnectionTimeout = 5,
            DefaultCommandTimeout = 30
        };

        return builder.ConnectionString.TrimEnd(';') + ";CharSet=utf8mb4";
    }
}
