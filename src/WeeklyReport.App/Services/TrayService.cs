using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace WeeklyReport.App.Services;

public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Icon _appIcon;

    public TrayService(Action showMain, Action toggleFloating, Action exit)
    {
        _menu = new Forms.ContextMenuStrip();
        _menu.Items.Add("打开周报", null, (_, _) => showMain());
        _menu.Items.Add("显示或隐藏悬浮窗", null, (_, _) => toggleFloating());
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("退出", null, (_, _) => exit());

        var resource = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/Assets/app.ico"))
            ?? throw new InvalidOperationException("未能加载应用图标。");
        using (resource.Stream)
        {
            _appIcon = new Icon(resource.Stream);
        }

        _icon = new Forms.NotifyIcon
        {
            Icon = _appIcon,
            Text = "周报",
            ContextMenuStrip = _menu,
            Visible = true
        };
        _icon.DoubleClick += (_, _) => showMain();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        _appIcon.Dispose();
    }
}
