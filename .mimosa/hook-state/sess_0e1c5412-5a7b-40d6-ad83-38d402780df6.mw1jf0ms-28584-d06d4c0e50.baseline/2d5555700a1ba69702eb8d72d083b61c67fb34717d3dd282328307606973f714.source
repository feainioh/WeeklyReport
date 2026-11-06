// [新增-机器人交互] 单击/拖动阈值测试：阈值内不拖动，任一轴达到阈值即拖动。
using System.Windows;
using WeeklyReport.App.Controls;

namespace WeeklyReport.DesktopTests;

public sealed class SpriteGestureTests
{
    [Fact]
    public void ExceedsDragThreshold_ReturnsFalseInsideBothThresholds()
    {
        Assert.False(SpriteGesture.ExceedsDragThreshold(
            new Point(10, 10), new Point(13, 12), 4, 4));
    }

    [Fact]
    public void ExceedsDragThreshold_ReturnsTrueOnEitherAxis()
    {
        Assert.True(SpriteGesture.ExceedsDragThreshold(
            new Point(10, 10), new Point(14, 10), 4, 4));
        Assert.True(SpriteGesture.ExceedsDragThreshold(
            new Point(10, 10), new Point(10, 15), 4, 4));
    }
}
