// [新增-形象动画2] 两段手臂挥手关键姿态保持在窗口和面罩边界外。
namespace WeeklyReport.DesktopTests;

public sealed class SpriteArmGeometryTests
{
    [Theory]
    [InlineData(16, 8)]
    [InlineData(90, -90)]
    [InlineData(155, 0)]
    [InlineData(162, 10)]
    public void LeftWavePoses_StayInsideWindow(double shoulder, double elbow)
    {
        var bounds = HandBounds.Left(shoulder, elbow);

        Assert.InRange(bounds.Left, 1.0, 74.0);
        Assert.InRange(bounds.Right, 1.0, 74.0);
        Assert.InRange(bounds.Top, 0.0, 95.0);
        Assert.InRange(bounds.Bottom, 0.0, 95.0);
    }

    [Fact]
    public void LeftWaveApex_StaysLeftOfVisor()
    {
        var bounds = HandBounds.Left(155, 0);

        Assert.True(bounds.Right <= 13.5);
        Assert.InRange(bounds.Top, 23.0, 38.0);
        Assert.InRange(bounds.Bottom, 23.0, 38.0);
    }

    private readonly record struct Bounds(double Left, double Top, double Right, double Bottom);

    private static class HandBounds
    {
        public static Bounds Left(double shoulder, double elbow)
        {
            const double shoulderX = 14;
            const double shoulderY = 48;
            const double upperArmLength = 9;
            const double forearmLength = 8;
            const double handRadius = 4;
            var shoulderRadians = shoulder * Math.PI / 180;
            var wristX = shoulderX - Math.Sin(shoulderRadians) * upperArmLength;
            var wristY = shoulderY + Math.Cos(shoulderRadians) * upperArmLength;
            var forearmRadians = (shoulder + elbow) * Math.PI / 180;
            var handX = wristX - Math.Sin(forearmRadians) * forearmLength;
            var handY = wristY + Math.Cos(forearmRadians) * forearmLength;

            return new Bounds(
                handX - handRadius,
                handY - handRadius,
                handX + handRadius,
                handY + handRadius);
        }
    }
}
