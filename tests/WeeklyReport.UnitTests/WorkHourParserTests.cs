using System.Globalization;
using WeeklyReport.Application;

namespace WeeklyReport.UnitTests;

public class WorkHourParserTests
{
    [Theory]
    [InlineData("1.5", 90)]
    [InlineData("0", 0)]
    [InlineData("0.0167", 1)]
    [InlineData("8.25", 495)]
    public void Parse_ConvertsHoursToMinutes(string text, int expected)
    {
        Assert.Equal(expected, WorkHourParser.Parse(text, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("bad")]
    [InlineData("0.001")]
    [InlineData("999999999999")]
    public void Parse_RejectsInvalidOrUnrepresentableValues(string text)
    {
        Assert.Throws<ArgumentException>(() => WorkHourParser.Parse(text, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Parse_EmptyValueIsOptional()
    {
        Assert.Null(WorkHourParser.Parse("  ", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Format_RoundTripsEveryMinuteInAnEightHourDay()
    {
        for (var minutes = 0; minutes <= 480; minutes++)
        {
            var text = WorkHourParser.Format(minutes, CultureInfo.InvariantCulture);
            Assert.Equal(minutes, WorkHourParser.Parse(text, CultureInfo.InvariantCulture));
        }
    }

    [Fact]
    public void Parse_UsesTheSpecifiedDecimalSeparator()
    {
        var culture = CultureInfo.GetCultureInfo("fr-FR");
        Assert.Equal(90, WorkHourParser.Parse("1,5", culture));
    }
}
