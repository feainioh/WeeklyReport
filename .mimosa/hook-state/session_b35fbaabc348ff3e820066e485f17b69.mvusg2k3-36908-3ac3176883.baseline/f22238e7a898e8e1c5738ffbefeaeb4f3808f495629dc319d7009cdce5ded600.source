using Microsoft.Win32;

namespace WeeklyReport.App.Services;

public sealed class AutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WeeklyReport";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return string.Equals(key?.GetValue(ValueName) as string, ExpectedCommand(), StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
            ?? throw new InvalidOperationException("无法打开当前用户的自启动设置。");
        var expected = ExpectedCommand();
        var existing = key.GetValue(ValueName) as string;
        if (existing is not null && !string.Equals(existing, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("已存在指向其他程序的同名自启动项，请先检查系统设置。");
        }

        if (enabled)
        {
            key.SetValue(ValueName, expected, RegistryValueKind.String);
        }
        else if (existing is not null)
        {
            key.DeleteValue(ValueName);
        }
    }

    private static string ExpectedCommand()
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法确定应用程序路径。");
        return $"\"{executable}\"";
    }
}
