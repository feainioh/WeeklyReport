using System.Net;
using System.Net.Sockets;
using WeeklyReport.Application;
using WeeklyReport.Domain;
using WeeklyReport.Infrastructure.Persistence;

namespace WeeklyReport.IntegrationTests;

public class MySqlConnectionFailureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConnectionFailure_IsReportedWithoutExposingPassword(bool useOrm)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var closeConnection = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync(timeout.Token);
            }, timeout.Token);
            var settings = new DatabaseConnectionSettings(
                "127.0.0.1", port, "weeklyreport_test_example", "test-user", "example-secret");

            var exception = useOrm
                ? await Assert.ThrowsAsync<DatabaseAccessException>(
                    () => new SqlSugarWorkEntryRepository(new FixedConfigurationStore(settings))
                        .GetByWeekAsync(WeekPeriod.FromDate(new DateOnly(2026, 10, 30))))
                : await Assert.ThrowsAsync<DatabaseAccessException>(
                    () => new MySqlDatabaseInitializer().TestConnectionAsync(settings, timeout.Token));
            await closeConnection;

            Assert.Contains("MySQL", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("example-secret", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            listener.Stop();
        }
    }

    private sealed class FixedConfigurationStore(DatabaseConnectionSettings settings) : IDatabaseConfigurationStore
    {
        public Task<DatabaseConnectionSettings?> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<DatabaseConnectionSettings?>(settings);

        public Task SaveAsync(DatabaseConnectionSettings value, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
