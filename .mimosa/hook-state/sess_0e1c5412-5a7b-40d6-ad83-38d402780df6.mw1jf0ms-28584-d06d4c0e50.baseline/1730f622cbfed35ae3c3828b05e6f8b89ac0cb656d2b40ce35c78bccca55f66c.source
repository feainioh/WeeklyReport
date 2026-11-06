// [新增-机器人交互] 拖动阈值判断：按下后只有移动超过系统拖动阈值才进入拖动，
// 原地松开视为单击。纯函数便于单元测试。
namespace WeeklyReport.App.Controls;

public static class SpriteGesture
{
    // [新增-机器人交互] 完全限定 Point：项目全局 using 引入 WinForms，避免与 Drawing.Point 二义性
    public static bool ExceedsDragThreshold(
        System.Windows.Point start, System.Windows.Point current,
        double horizontal, double vertical) =>
        Math.Abs(current.X - start.X) >= horizontal
        || Math.Abs(current.Y - start.Y) >= vertical;
}
