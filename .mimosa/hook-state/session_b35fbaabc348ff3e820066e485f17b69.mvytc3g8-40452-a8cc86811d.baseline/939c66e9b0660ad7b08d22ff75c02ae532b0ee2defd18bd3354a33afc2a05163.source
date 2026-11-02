using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using WeeklyReport.Application;
using WeeklyReport.Domain;

namespace WeeklyReport.App.Services;

public sealed class ReminderMonitor(
    ReminderEngine engine,
    ReminderSchedule schedule,
    TimeProvider timeProvider,
    Action<string> reportStatus,
    ILogger<ReminderMonitor> logger) : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _signal = new(0, 1);
    private Task? _loop;
    private string? _lastSent;

    public void Start()
    {
        if (_loop is not null)
        {
            throw new InvalidOperationException("提醒服务已经启动。");
        }

        SystemEvents.TimeChanged += OnTimeChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _loop = Task.Run(() => RunAsync(_stop.Token));
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var delay = TimeSpan.FromSeconds(30);
                try
                {
                    var now = timeProvider.GetUtcNow();
                    var timeZone = TimeZoneInfo.Local;
                    var result = await engine.EvaluateAsync(now, timeZone, cancellationToken);
                    if (result.NotificationsUnavailable)
                    {
                        reportStatus("系统通知不可用，请在 Windows 设置中启用；应用退出后不会准点提醒。");
                    }
                    else if (result.Failures.Count > 0)
                    {
                        reportStatus("提醒发送失败，请检查 Windows 通知设置；本次不会自动重复发送。");
                    }
                    else if (result.HasUncertainPreviousDelivery)
                    {
                        reportStatus("部分提醒发送状态不确定，未自动重发；请手动检查周报。");
                    }
                    else
                    {
                        if (result.Sent.Count > 0)
                        {
                            var latest = result.Sent[^1];
                            _lastSent = $"已向系统提交 {latest.Key.WeekStart:yyyy-MM-dd} 所在周的提醒；实际显示受系统设置影响。";
                        }

                        reportStatus(_lastSent ?? "提醒已启用：周四 17:00、周五 10:00；应用退出后不会准点提醒。");
                    }

                    var next = schedule.GetNext(now, timeZone, new HashSet<ReminderKey>());
                    delay = next.DueAt - now;
                    if (delay < TimeSpan.FromMilliseconds(100))
                    {
                        delay = TimeSpan.FromMilliseconds(100);
                    }
                    else if (delay > TimeSpan.FromMinutes(1))
                    {
                        delay = TimeSpan.FromMinutes(1);
                    }
                }
                catch (Exception exception) when (IsExpected(exception))
                {
                    logger.LogWarning("Reminder check failed: {ErrorType}", exception.GetType().Name);
                    reportStatus("提醒检查失败，请检查本地提醒状态和 Windows 通知设置。");
                }

                await _signal.WaitAsync(delay, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError("Reminder service stopped unexpectedly: {ErrorType}", exception.GetType().Name);
            reportStatus("提醒服务意外停止，请重新启动应用并检查日志。");
        }
    }

    private static bool IsExpected(Exception exception) =>
        exception is ReminderNotificationException or IOException or UnauthorizedAccessException
            or CryptographicException or JsonException or InvalidOperationException or ArgumentException;

    private void OnTimeChanged(object? sender, EventArgs e)
    {
        TimeZoneInfo.ClearCachedData();
        Signal();
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            Signal();
        }
    }

    private void Signal()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // A pending signal already causes an immediate recalculation.
        }
        catch (ObjectDisposedException) when (_stop.IsCancellationRequested)
        {
            // A system event raced with service shutdown.
        }
    }

    public async ValueTask DisposeAsync()
    {
        SystemEvents.TimeChanged -= OnTimeChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        await _stop.CancelAsync();
        if (_loop is not null)
        {
            await _loop;
        }

        _stop.Dispose();
        _signal.Dispose();
    }
}
