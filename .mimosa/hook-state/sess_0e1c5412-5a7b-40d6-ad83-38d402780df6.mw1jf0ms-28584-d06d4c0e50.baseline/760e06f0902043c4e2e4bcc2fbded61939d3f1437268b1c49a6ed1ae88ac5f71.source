using System.Globalization;

namespace WeeklyReport.Application;

public static class WorkHourParser
{
    public static int? Parse(string? text, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        const NumberStyles styles = NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite
            | NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
        if (!decimal.TryParse(text, styles, culture, out var hours) || hours < 0 || hours > int.MaxValue / 60m)
        {
            throw new ArgumentException("请输入有效的非负工时（小时）。", nameof(text));
        }

        var minutes = decimal.Round(hours * 60m, 0, MidpointRounding.AwayFromZero);
        if (hours > 0 && minutes == 0)
        {
            throw new ArgumentException("工时精确到分钟，请输入至少一分钟。", nameof(text));
        }

        return (int)minutes;
    }

    public static string Format(int? minutes, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        if (minutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minutes));
        }

        return minutes is null ? string.Empty : (minutes.Value / 60m).ToString("0.####", culture);
    }
}
