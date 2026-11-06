using System.Globalization;
using WeeklyReport.Domain;

namespace WeeklyReport.Application;

public static class ReminderActivation
{
    public static bool TryParse(string? arguments, out DateOnly weekStart)
    {
        weekStart = default;
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return false;
        }

        if (arguments.Contains(';') && arguments.Contains('&'))
        {
            return false;
        }

        var separator = arguments.Contains(';') ? ';' : '&';
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in arguments.Split(separator))
        {
            var equals = part.IndexOf('=');
            if (equals <= 0 || !values.TryAdd(part[..equals], part[(equals + 1)..]))
            {
                return false;
            }
        }

        return TryParseValues(values, out weekStart);
    }

    public static bool TryParseValues(IDictionary<string, string> values, out DateOnly weekStart)
    {
        ArgumentNullException.ThrowIfNull(values);
        weekStart = default;
        if (values.Count != 2
            || !values.TryGetValue("action", out var action) || action != "weekly-report"
            || !values.TryGetValue("week", out var week)
            || !DateOnly.TryParseExact(week, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return false;
        }

        try
        {
            _ = new WeekPeriod(date);
            weekStart = date;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
