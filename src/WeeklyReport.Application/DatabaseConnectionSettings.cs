using System.Net;
using System.Text.RegularExpressions;

namespace WeeklyReport.Application;

public sealed class DatabaseConnectionSettings
{
    public DatabaseConnectionSettings(string host, int port, string database, string userName, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentNullException.ThrowIfNull(password);

        host = host.Trim();
        database = database.Trim();
        userName = userName.Trim();

        if (!host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            && (!IPAddress.TryParse(host, out var address) || !IPAddress.IsLoopback(address)))
        {
            throw new ArgumentException("首版仅支持连接本机 MySQL。", nameof(host));
        }

        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "端口必须在 1 到 65535 之间。");
        }

        if (!Regex.IsMatch(database, "^[A-Za-z0-9_]{1,64}$", RegexOptions.CultureInvariant))
        {
            throw new ArgumentException("数据库名称只能包含字母、数字和下划线，最长 64 个字符。", nameof(database));
        }

        Host = host;
        Port = port;
        Database = database;
        UserName = userName;
        Password = password;
    }

    public string Host { get; }
    public int Port { get; }
    public string Database { get; }
    public string UserName { get; }
    public string Password { get; }

    public override string ToString() => $"{UserName}@{Host}:{Port}/{Database}";
}
