using System.Globalization;
using Microsoft.Data.Sqlite;
using WeeklyReport.Domain;

namespace WeeklyReport.Infrastructure.Persistence.Sqlite;

/// <summary>Shares the UTC timestamp formats used by SQLite persistence and synchronization.</summary>
internal static class SqliteTimestamp
{
    private const string SqliteFormat = "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'";
    private const string MySqlFormat = "yyyy-MM-dd HH:mm:ss.ffffff";

    internal static string Format(DateTimeOffset value)
    {
        var utc = StorageDateRange.NormalizeUtcTimestamp(value).UtcDateTime;
        return utc.ToString(SqliteFormat, CultureInfo.InvariantCulture);
    }

    internal static string FormatUtcNow() => Format(DateTimeOffset.UtcNow);

    internal static DateTimeOffset Parse(string value) =>
        DateTimeOffset.ParseExact(
            value,
            SqliteFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    internal static DateTimeOffset? ReadNullable(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Parse(reader.GetString(ordinal));

    internal static string ToMySqlDateTime(DateTimeOffset value) =>
        FormatMySqlDateTime(StorageDateRange.NormalizeUtcTimestamp(value));

    private static string FormatMySqlDateTime(DateTimeOffset value) =>
        value.ToString(MySqlFormat, CultureInfo.InvariantCulture);
}
