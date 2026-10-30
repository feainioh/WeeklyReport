using System.IO;
using System.Runtime.InteropServices;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using WeeklyReport.Application;
using WeeklyReport.Domain;

namespace WeeklyReport.App.Services;

public sealed class WindowsReminderNotifier(ILogger<WindowsReminderNotifier> logger) : IReminderNotifier, IDisposable
{
    private AppNotificationManager? _manager;

    public event Action<DateOnly>? Activated;

    public void Register()
    {
        try
        {
            var manager = AppNotificationManager.Default;
            manager.NotificationInvoked += OnNotificationInvoked;
            try
            {
                manager.Register();
                _manager = manager;
            }
            catch
            {
                manager.NotificationInvoked -= OnNotificationInvoked;
                throw;
            }
        }
        catch (Exception exception) when (IsNotificationFailure(exception))
        {
            throw new ReminderNotificationException("无法注册系统通知，请检查 Windows 通知权限和运行环境。", exception);
        }
    }

    public static async Task RemoveRegistrationAsync()
    {
        try
        {
            var manager = AppNotificationManager.Default;
            manager.NotificationInvoked += IgnoreNotificationDuringRemoval;
            try
            {
                manager.Register();
                try
                {
                    await manager.RemoveAllAsync();
                }
                finally
                {
                    manager.UnregisterAll();
                }
            }
            finally
            {
                manager.NotificationInvoked -= IgnoreNotificationDuringRemoval;
            }
        }
        catch (Exception exception) when (IsNotificationFailure(exception))
        {
            throw new ReminderNotificationException("无法清理系统通知，请检查 Windows 通知权限和运行环境。", exception);
        }
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return Task.FromResult(_manager is not null && _manager.Setting == AppNotificationSetting.Enabled);
        }
        catch (Exception exception) when (IsNotificationFailure(exception))
        {
            throw new ReminderNotificationException("无法读取系统通知状态，请检查 Windows 通知设置。", exception);
        }
    }

    public Task SendAsync(ScheduledReminder reminder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reminder);
        return ShowAsync(new WeekPeriod(reminder.Key.WeekStart), isTest: false, cancellationToken);
    }

    public Task SendTestAsync(DateOnly date, CancellationToken cancellationToken = default) =>
        ShowAsync(WeekPeriod.FromDate(date), isTest: true, cancellationToken);

    private Task ShowAsync(WeekPeriod week, bool isTest, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var manager = _manager
                ?? throw new ReminderNotificationException("系统通知尚未注册。");
            if (manager.Setting != AppNotificationSetting.Enabled)
            {
                throw new ReminderNotificationException("系统通知已关闭，请在 Windows 设置中启用。");
            }

            var notification = new AppNotificationBuilder()
                .AddArgument("action", "weekly-report")
                .AddArgument("week", week.Start.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))
                .AddText(isTest ? "周报测试提醒" : "周报提醒")
                .AddText(isTest
                    ? "这是一条测试提醒；点击可打开本周周报。"
                    : $"请整理 {week.Start:yyyy-MM-dd} 至 {week.EndInclusive:yyyy-MM-dd} 的周报。")
                .BuildNotification();
            var arguments = XDocument.Parse(notification.Payload).Root?.Attribute("launch")?.Value;
            if (!ReminderActivation.TryParse(arguments, out var activatedWeek) || activatedWeek != week.Start)
            {
                throw new ReminderNotificationException("系统提醒参数无效，未发送通知。");
            }

            manager.Show(notification);
            return Task.CompletedTask;
        }
        catch (Exception exception) when (IsNotificationFailure(exception))
        {
            throw new ReminderNotificationException("无法发送系统通知，请检查 Windows 通知设置。", exception);
        }
        catch (XmlException exception)
        {
            throw new ReminderNotificationException("系统提醒内容无效，未发送通知。", exception);
        }
    }

    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        try
        {
            if (ReminderActivation.TryParse(args.Argument, out var week)
                && ReminderActivation.TryParseValues(args.Arguments, out var parsedWeek)
                && week == parsedWeek)
            {
                Activated?.Invoke(week);
            }
            else
            {
                logger.LogWarning("Ignored an invalid reminder activation.");
            }
        }
        catch (Exception exception) when (IsNotificationFailure(exception))
        {
            logger.LogWarning("Reminder activation failed: {ErrorType}", exception.GetType().Name);
        }
    }

    private static void IgnoreNotificationDuringRemoval(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
    }

    private static bool IsNotificationFailure(Exception exception) =>
        exception is COMException or InvalidOperationException or ArgumentException or FormatException
            or UnauthorizedAccessException
            or DllNotFoundException or TypeLoadException or TypeInitializationException
            or FileLoadException or FileNotFoundException or BadImageFormatException or NotSupportedException;

    public void Dispose()
    {
        if (_manager is null)
        {
            return;
        }

        try
        {
            _manager.NotificationInvoked -= OnNotificationInvoked;
            _manager.Unregister();
        }
        catch (Exception exception) when (IsNotificationFailure(exception))
        {
            throw new ReminderNotificationException("无法清理系统通知注册，请检查 Windows 应用设置。", exception);
        }
        finally
        {
            _manager = null;
        }
    }
}
