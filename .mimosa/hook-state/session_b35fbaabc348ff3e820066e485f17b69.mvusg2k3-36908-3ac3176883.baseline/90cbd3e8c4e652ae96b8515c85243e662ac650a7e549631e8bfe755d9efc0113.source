using System.Text;

namespace WeeklyReport.Infrastructure.Persistence;

internal static class DbTextLimits
{
    private const int MediumTextMaximumBytes = 16_777_215;
    private static readonly UTF8Encoding Utf8 = new(false, throwOnInvalidBytes: true);

    public static void Validate(string value, string field)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (Utf8.GetByteCount(value) > MediumTextMaximumBytes)
        {
            throw new ArgumentException($"{field}超过数据库允许的大小。", field);
        }
    }
}
