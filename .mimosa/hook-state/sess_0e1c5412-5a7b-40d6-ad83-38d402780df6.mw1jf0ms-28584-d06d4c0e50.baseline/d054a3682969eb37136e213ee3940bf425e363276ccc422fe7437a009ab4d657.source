// [新增-界面现代化2] Win11 标题栏着色辅助：经 DWM DWMWA_CAPTION_COLOR 将系统标题栏染成应用窗口底色，
// 让标题栏与界面融为一体；Win10 或非桌面环境自动忽略，保持系统默认标题栏。
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace WeeklyReport.App.Services;

public static class WindowChromeHelper
{
    private const int DwmwaCaptionColor = 35;

    /// <summary>按当前 WindowBrush 为主题窗口着色标题栏；高对比度、Win10 与调用失败时静默跳过。</summary>
    public static void ApplyCaptionColor(Window window)
    {
        if (SystemParameters.HighContrast || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || window.TryFindResource("WindowBrush") is not SolidColorBrush brush)
        {
            return;
        }

        // [新增-界面现代化2] DWM 使用 COLORREF（0x00BBGGRR），与 WPF 的 RGBA 通道顺序不同
        var color = brush.Color;
        var colorRef = color.R | (color.G << 8) | (color.B << 16);
        try
        {
            _ = DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref colorRef, Marshal.SizeOf<int>());
        }
        catch (DllNotFoundException)
        {
            // 无 dwmapi.dll 的环境（如部分测试宿主）保持系统默认标题栏。
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
