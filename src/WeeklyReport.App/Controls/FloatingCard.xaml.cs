// [新增-悬浮精灵] 切片 1：悬浮卡片内容控件（自 FloatingWindow 抽取），对外暴露与原行为一致的事件入口。
using System.Windows;
using System.Windows.Controls;

namespace WeeklyReport.App.Controls;

// [新增-悬浮精灵] 完全限定 UserControl：项目全局 using 引入 WinForms 命名空间，避免与 Forms.UserControl 二义性
public partial class FloatingCard : System.Windows.Controls.UserControl
{
    /// <summary>用户点击"打开记录"。</summary>
    public event EventHandler? OpenRequested;

    /// <summary>用户点击"收起"。</summary>
    public event EventHandler? HideRequested;

    /// <summary>用户在标题栏按下鼠标（由宿主窗口执行 DragMove 与位置保存）。</summary>
    public event EventHandler? HeaderDragPressed;

    public FloatingCard()
    {
        InitializeComponent();
    }

    public void SetWeek(string text)
    {
        WeekText.Text = text;
    }

    public void SetReminderStatus(string message)
    {
        ReminderStatusText.Text = message;
        ReminderStatusText.ToolTip = message;
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        HeaderDragPressed?.Invoke(this, EventArgs.Empty);
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        HideRequested?.Invoke(this, EventArgs.Empty);
    }
}
