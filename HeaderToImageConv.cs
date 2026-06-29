using HydroExplorer.View;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;


namespace HydroExplorer
{
    [ValueConversion(typeof(string), typeof(BitmapImage))]
    public class HeaderToImageConv : IValueConverter
    {
        public static readonly HeaderToImageConv Instance = new();


        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var path = (string)value;

            if (path == null)
                return null;

            var name = TreeViewControl.GetFileFolderName(path);

            var image = "/Images/file.png";

            if (string.IsNullOrEmpty(name))
                image = "/Images/drive.png";

            else if (new FileInfo(path).Attributes.HasFlag(FileAttributes.Directory))
                image = "/Images/folder_closed.ico";

            else if (path.EndsWith(".prj", StringComparison.OrdinalIgnoreCase))
                image = "/Images/hecras.png";

            else if (path.EndsWith(".hms", StringComparison.OrdinalIgnoreCase))
                image = "/Images/hechms.png";

            else if (path.EndsWith(".shp", StringComparison.OrdinalIgnoreCase))
                image = "/Images/shp.png";

            else if (new FileInfo(path).Attributes.HasFlag(FileAttributes.Directory))
                image = "/Images/folder_closed.ico";

            //BitmapImage myImage = new BitmapImage(new Uri("Z:/10 DEV/hydroExplorer/Images/folder_closed.png"));
            BitmapImage myImage = new(new Uri($"pack://application:,,,{image}"));
            return myImage;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}