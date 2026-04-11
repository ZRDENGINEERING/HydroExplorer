using HydroExplorer.View;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;



namespace HydroExplorer.Helpers
{
    [ValueConversion(typeof(string), typeof(BitmapImage))]
    public class ConvValueToColor : IValueConverter
    {

        public ConvValueToColor InstanceValToCol = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();

            //return (double)value < 0 ? Brushes.Red : Brushes.Green;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}