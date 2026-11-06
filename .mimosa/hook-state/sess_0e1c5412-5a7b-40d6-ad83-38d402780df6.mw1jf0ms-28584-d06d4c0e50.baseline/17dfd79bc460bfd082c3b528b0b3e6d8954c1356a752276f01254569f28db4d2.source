// [新增-去嵌套] 索引到布尔值转换器：内层页签改为 RadioButton 分段开关后，
// 继续以 ViewModel 的 SelectedTabIndex 作为唯一选中状态来源（ConverterParameter 指定对应索引）。
using System.Globalization;
using System.Windows.Data;

namespace WeeklyReport.App.Converters;

public sealed class IndexToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int index
        && TryParse(parameter, out var expected)
        && index == expected;

    // [新增-去嵌套] 完全限定 Binding：项目全局 using 引入 WinForms，避免与 Forms.Binding 二义性
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true && TryParse(parameter, out var index)
            ? index
            : System.Windows.Data.Binding.DoNothing;

    private static bool TryParse(object parameter, out int value) =>
        int.TryParse(parameter as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
}
