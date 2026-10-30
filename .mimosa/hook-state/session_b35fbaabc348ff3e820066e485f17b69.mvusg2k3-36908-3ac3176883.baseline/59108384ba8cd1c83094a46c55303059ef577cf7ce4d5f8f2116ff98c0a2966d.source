using WeeklyReport.Application;
using WeeklyReport.Domain;

namespace WeeklyReport.UnitTests;

public class WeeklyReportDraftBuilderTests
{
    private readonly WeeklyReportDraftBuilder _builder = new();
    private readonly WeekPeriod _week = new(new DateOnly(2026, 10, 26));
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_AggregatesSelectedWeekAndDistinguishesHourStates()
    {
        var manual = new[]
        {
            Entry(new DateOnly(2026, 10, 26), "手工工作", 90),
            Entry(new DateOnly(2026, 11, 1), "未填工时", null),
            Entry(new DateOnly(2026, 11, 2), "下周工作", 120)
        };
        var git = new[]
        {
            GitItem(new DateOnly(2026, 10, 30), "已确认", 120, 60),
            GitItem(new DateOnly(2026, 10, 31), "自动估算", 45, null),
            GitItem(new DateOnly(2026, 11, 1), "无法推断", null, null)
        };

        var draft = _builder.Build(_week, manual, git);

        Assert.Equal(150, draft.ConfirmedMinutes);
        Assert.Equal(45, draft.EstimatedMinutes);
        Assert.Equal(2, draft.UnknownHoursCount);
        Assert.Contains("手工工作", draft.WorkSummary);
        Assert.Contains("[项目] 自动估算", draft.WorkSummary);
        Assert.DoesNotContain("下周工作", draft.WorkSummary);
    }

    [Fact]
    public void Build_RejectsDuplicateSourceIdentifiers()
    {
        var id = Guid.NewGuid();
        var manual = new WorkEntry(id, _week.Start, "手工", Now, Now);
        var git = new GitReportWorkItem(id, Guid.NewGuid(), "项目", _week.Start, "Git", 60, null);

        Assert.Throws<ArgumentException>(() => _builder.Build(_week, [manual], [git]));
    }

    [Fact]
    public void Build_ReportsHourOverflow()
    {
        var manual = new[]
        {
            Entry(_week.Start, "第一项", int.MaxValue),
            Entry(_week.Start, "第二项", 1)
        };

        Assert.Throws<InvalidOperationException>(() => _builder.Build(_week, manual, []));
    }

    private static WorkEntry Entry(DateOnly date, string content, int? minutes) =>
        new(Guid.NewGuid(), date, content, Now, Now, minutes);

    private static GitReportWorkItem GitItem(DateOnly date, string description, int? estimated, int? confirmed) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "项目", date, description, estimated, confirmed);
}
