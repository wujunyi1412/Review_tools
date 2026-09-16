using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ImageReviewTool.Infrastructure;

public sealed class SameTagConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is string current && values[1] is string option
        && string.Equals(current, option, StringComparison.OrdinalIgnoreCase);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
