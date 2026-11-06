// [新增-形象动画2] 动作优先级：同一时刻只允许一个全身动作，高优先级可抢占低优先级。
using WeeklyReport.App.Services;

namespace WeeklyReport.DesktopTests;

public sealed class SpriteActionPriorityTests
{
    [Theory]
    [InlineData(null, SpriteActionPriority.Ambient, true)]
    [InlineData(SpriteActionPriority.State, SpriteActionPriority.User, false)]
    [InlineData(SpriteActionPriority.User, SpriteActionPriority.Celebration, false)]
    [InlineData(SpriteActionPriority.Celebration, SpriteActionPriority.User, true)]
    [InlineData(SpriteActionPriority.Automatic, SpriteActionPriority.Ambient, false)]
    [InlineData(SpriteActionPriority.Ambient, SpriteActionPriority.Automatic, true)]
    [InlineData(SpriteActionPriority.User, SpriteActionPriority.User, false)]
    public void CanStart_RespectsPriority(
        SpriteActionPriority? active, SpriteActionPriority next, bool expected)
    {
        Assert.Equal(expected, SpriteActionPriorityRules.CanStart(active, next));
    }
}
