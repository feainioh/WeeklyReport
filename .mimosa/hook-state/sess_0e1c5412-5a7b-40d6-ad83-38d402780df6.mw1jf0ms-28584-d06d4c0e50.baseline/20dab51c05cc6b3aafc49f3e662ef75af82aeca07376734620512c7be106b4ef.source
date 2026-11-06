using WeeklyReport.App.Services;
using WeeklyReport.Application;

namespace WeeklyReport.DesktopTests;

public sealed class WeeklyAdvicePolicyTests
{
    private static readonly DateOnly WeekStart = new(2026, 11, 2);

    [Fact]
    public void CanShow_ProactiveAdviceRequiresIdleVisibleEnabledAndNotMuted()
    {
        var now = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        var settings = SpriteBehaviorSettings.Default;

        Assert.True(WeeklyAdvicePolicy.CanShow(settings, now, TimeSpan.FromMinutes(5),
            true, false, false, userInitiated: false));
        Assert.False(WeeklyAdvicePolicy.CanShow(settings, now, TimeSpan.FromMinutes(4),
            true, false, false, userInitiated: false));
        Assert.False(WeeklyAdvicePolicy.CanShow(settings, now, TimeSpan.FromMinutes(5),
            false, false, false, userInitiated: false));
        Assert.False(WeeklyAdvicePolicy.CanShow(settings, now, TimeSpan.FromMinutes(5),
            true, true, false, userInitiated: false));
        Assert.False(WeeklyAdvicePolicy.CanShow(settings with { DataAdvice = false }, now,
            TimeSpan.FromMinutes(5), true, false, false, userInitiated: false));
        Assert.False(WeeklyAdvicePolicy.CanShow(settings, now, TimeSpan.FromMinutes(5),
            true, false, true, userInitiated: false));
    }

    [Fact]
    public void CanShow_ProactiveAdviceRespectsQuietHoursAndDailyLimit()
    {
        var settings = SpriteBehaviorSettings.Default;

        Assert.False(WeeklyAdvicePolicy.CanShow(settings,
            new DateTimeOffset(2026, 11, 2, 23, 0, 0, TimeSpan.Zero),
            TimeSpan.FromMinutes(5), true, false, false, userInitiated: false));
        Assert.False(WeeklyAdvicePolicy.CanShow(
            settings with { LastAdviceDate = new DateOnly(2026, 11, 2) },
            new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero),
            TimeSpan.FromMinutes(5), true, false, false, userInitiated: false));
    }

    [Fact]
    public void CanShow_UserInitiatedAdviceBypassesQuietHoursDailyLimitAndMute()
    {
        var settings = SpriteBehaviorSettings.Default with
        {
            DataAdvice = false,
            LastAdviceDate = new DateOnly(2026, 11, 2)
        };

        Assert.True(WeeklyAdvicePolicy.CanShow(settings,
            new DateTimeOffset(2026, 11, 2, 23, 0, 0, TimeSpan.Zero),
            TimeSpan.Zero, true, false, true, userInitiated: true));
        Assert.False(WeeklyAdvicePolicy.CanShow(settings,
            new DateTimeOffset(2026, 11, 2, 23, 0, 0, TimeSpan.Zero),
            TimeSpan.Zero, true, true, true, userInitiated: true));
    }

    [Fact]
    public void ForPreset_UsesAdviceDefaultsAndPreservesDailyLimit()
    {
        var lastAdvice = new DateOnly(2026, 11, 1);
        var current = SpriteBehaviorSettings.Default with
        {
            LastAdviceDate = lastAdvice,
            GuideShown = true
        };

        var quiet = SpriteBehaviorSettings.ForPreset(SpriteBehaviorPreset.Quiet, current);
        var gentle = SpriteBehaviorSettings.ForPreset(SpriteBehaviorPreset.Gentle, quiet);
        var lively = SpriteBehaviorSettings.ForPreset(SpriteBehaviorPreset.Lively, gentle);

        Assert.False(quiet.DataAdvice);
        Assert.True(gentle.DataAdvice);
        Assert.True(lively.DataAdvice);
        Assert.All(new[] { quiet, gentle, lively }, settings =>
        {
            Assert.Equal(lastAdvice, settings.LastAdviceDate);
            Assert.True(settings.GuideShown);
        });
    }

    [Theory]
    [InlineData(WeeklyStorageStatus.MySqlNotConfigured)]
    [InlineData(WeeklyStorageStatus.MySqlUnavailable)]
    [InlineData(WeeklyStorageStatus.LocalUnreadable)]
    [InlineData(WeeklyStorageStatus.Unknown)]
    public void Create_PrioritizesStorageFailures(WeeklyStorageStatus status)
    {
        var advice = WeeklyAdvicePolicy.Create(Context(
            storage: status, manualCount: 0, gitCount: 0, saved: false, unknownHours: 0));

        Assert.Equal(WeeklyAdviceKind.StorageUnavailable, advice.Kind);
        Assert.Equal(WeeklyAdviceAction.StorageSettings, advice.Action);
    }

    [Fact]
    public void Create_PrioritizesConflictsOverPendingAndBusinessAdvice()
    {
        var advice = WeeklyAdvicePolicy.Create(Context(
            storage: WeeklyStorageStatus.Conflict,
            manualCount: 3,
            gitCount: 2,
            saved: false,
            unknownHours: 1,
            pending: 4,
            conflicts: 2));

        Assert.Equal(WeeklyAdviceKind.ConflictPending, advice.Kind);
        Assert.Equal(WeeklyAdviceAction.SyncStatus, advice.Action);
        Assert.Contains("2 项", advice.Text);
    }

    [Fact]
    public void Create_PrioritizesPendingSyncOverBusinessAdvice()
    {
        var advice = WeeklyAdvicePolicy.Create(Context(
            storage: WeeklyStorageStatus.SyncPending,
            manualCount: 1,
            gitCount: 1,
            saved: false,
            unknownHours: 0,
            pending: 3));

        Assert.Equal(WeeklyAdviceKind.SyncPending, advice.Kind);
        Assert.Contains("3 项", advice.Text);
    }

    [Fact]
    public void Create_DoesNotInferBusinessStateFromIncompleteContext()
    {
        var advice = WeeklyAdvicePolicy.Create(Context(
            manualCount: null, gitCount: 0, saved: false, unknownHours: 0));

        Assert.Equal(WeeklyAdviceKind.StorageUnavailable, advice.Kind);
        Assert.DoesNotContain("没有工作记录", advice.Text);
    }

    [Fact]
    public void Create_SuggestsCheckingUnknownHoursBeforeReportState()
    {
        var advice = WeeklyAdvicePolicy.Create(Context(
            manualCount: 1, gitCount: 2, saved: false, unknownHours: 2));

        Assert.Equal(WeeklyAdviceKind.UnknownHours, advice.Kind);
        Assert.Equal(WeeklyAdviceAction.GitWorkItems, advice.Action);
        Assert.Contains("2 项", advice.Text);
    }

    [Fact]
    public void Create_SuggestsRecordingWorkWhenWeekIsEmptyAndReportNotSaved()
    {
        var advice = WeeklyAdvicePolicy.Create(Context(
            manualCount: 0, gitCount: 0, saved: false, unknownHours: 0));

        Assert.Equal(WeeklyAdviceKind.NoWorkItems, advice.Kind);
        Assert.Equal(WeeklyAdviceAction.WorkEntries, advice.Action);
    }

    [Fact]
    public void Create_SuggestsPreparingReportWhenWorkExistsAndReportIsNotSaved()
    {
        var advice = WeeklyAdvicePolicy.Create(Context(
            manualCount: 1, gitCount: 0, saved: false, unknownHours: 0));

        Assert.Equal(WeeklyAdviceKind.ReportNotSaved, advice.Kind);
        Assert.Equal(WeeklyAdviceAction.WeeklyReport, advice.Action);
    }

    [Fact]
    public void Create_SuggestsReviewWhenReportIsAlreadySaved()
    {
        var advice = WeeklyAdvicePolicy.Create(Context(
            manualCount: 0, gitCount: 0, saved: true, unknownHours: 0));

        Assert.Equal(WeeklyAdviceKind.ReportSavedCheck, advice.Kind);
        Assert.Equal(WeeklyAdviceAction.WeeklyReport, advice.Action);
    }

    private static WeeklyProgressContext Context(
        WeeklyStorageStatus storage = WeeklyStorageStatus.MySqlHealthy,
        int? manualCount = 1,
        int? gitCount = 0,
        bool? saved = false,
        int? unknownHours = 0,
        int pending = 0,
        int conflicts = 0) =>
        new(WeekStart, manualCount, gitCount, saved, unknownHours,
            storage, pending, conflicts, null, null);
}
