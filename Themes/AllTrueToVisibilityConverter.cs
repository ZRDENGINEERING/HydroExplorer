using System.Globalization;
using System.Windows;
using System.Windows.Data;


namespace HydroExplorer.Themes
{
    /// <summary>
    /// Multi-value visibility converter: returns Visible only if every bound value
    /// is a boolean true. Any false, non-boolean, or null value yields Collapsed.
    /// Used where a single Visibility binding needs to depend on more than one
    /// boolean condition at once (e.g. ShowProfilePlot AND HasMultipleReaches).
    /// </summary>
    public class AllTrueToVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            bool allTrue = values is { Length: > 0 } &&
                values.All(v => v is bool b && b);

            return allTrue ? Visibility.Visible : Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}