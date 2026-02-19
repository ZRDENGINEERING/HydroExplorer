using HydroExplorer.MVVM.ViewModels;
//using HydroExplorer.MVVM.Models;
using System.Globalization;
using System.IO;
using System.Windows.Data;
//using System.Windows.Forms;
using System.Windows.Media.Imaging;


namespace HydroExplorer
{
    [ValueConversion(typeof(string), typeof(BitmapImage))]
    public class HeaderToImageConv : IValueConverter
    {

        public static HeaderToImageConv Instance = new HeaderToImageConv();

        //TreeNode _treeNode = new();

        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var path = (string)value;

            if (path == null)
                return null;

            var name = MainViewModel.GetFileFolderName(path);

            var image = "/Images/file.png";

            if (string.IsNullOrEmpty(name))
                image = "/Images/drive.png";

            else if (new FileInfo(path).Attributes.HasFlag(FileAttributes.Directory))
                image = "/Images/folder_closed.png";

            //BitmapImage myImage = new BitmapImage(new Uri("Z:/10 DEV/hydroExplorer/Images/folder_closed.png"));
            BitmapImage myImage = new BitmapImage(new Uri($"pack://application:,,,{image}"));
            return myImage;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
