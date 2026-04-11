using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;


namespace HydroExplorer.Helpers
{
    class ValueToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d)
                return d > 0 ? Brushes.Red : (d < 0 ? Brushes.LimeGreen : Brushes.Gray);

            return Brushes.White; 
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
               => throw new NotImplementedException();
    }
}
