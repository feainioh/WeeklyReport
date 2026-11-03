// [新增-机器人交互] 本周周报保存状态：用于区分未保存（橙色提醒）、已保存（蓝色轻提示）
// 与查询失败/未配置数据库（通用文案）三种提醒呈现。
namespace WeeklyReport.App.Services;

public enum WeeklyReportSavedState
{
    Saved,
    NotSaved,
    Unknown
}
