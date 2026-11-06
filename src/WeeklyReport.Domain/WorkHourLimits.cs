namespace WeeklyReport.Domain;

/// <summary>Defines the shared work-hour thresholds used by estimation and entry validation.</summary>
public static class WorkHourLimits
{
    /// <summary>The maximum number of minutes automatically estimated or treated as a normal daily total.</summary>
    public const int DailyLimitMinutes = 8 * 60;
}
