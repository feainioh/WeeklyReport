namespace WeeklyReport.Domain;

public readonly record struct WeekPeriod
{
    public WeekPeriod(DateOnly start)
    {
        if (start.DayOfWeek != DayOfWeek.Monday)
        {
            throw new ArgumentException("The week must start on Monday.", nameof(start));
        }

        if (start.DayNumber > DateOnly.MaxValue.DayNumber - 7)
        {
            throw new ArgumentOutOfRangeException(nameof(start), "The end of the week must be representable.");
        }

        Start = start;
    }

    public DateOnly Start { get; }

    public DateOnly EndExclusive => Start.AddDays(7);

    public DateOnly EndInclusive => Start.AddDays(6);

    public bool Contains(DateOnly date) => date >= Start && date < EndExclusive;

    public static WeekPeriod FromDate(DateOnly date)
    {
        var daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
        return new WeekPeriod(date.AddDays(-daysSinceMonday));
    }
}
