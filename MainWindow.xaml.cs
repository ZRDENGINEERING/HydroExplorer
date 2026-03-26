using HydroExplorer.View;
using HydroExplorer.ViewModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;


namespace HydroExplorer
{
    public partial class MainWindow : Window
    {
        public MainWindowViewModel ViewModel { get; } = new MainWindowViewModel();

        public MainWindow()
        {
            InitializeComponent();
            DataContext = ViewModel;
        }


        public string CustomTitle { get; } = "Hello, World!";
        public string CustomLabel { get; } = "Dev Leader Rocks!";


        private void Folder_Expanded(object sender, RoutedEventArgs e)
        {
            var item = (TreeViewItem)sender;

            if (item.Items.Count != 1 || item.Items[0] != null)
                return;

            item.Items.Clear();

            var fullPath = (string)item.Tag;

            var directories = new List<string>();
            try
            {
                var dirs = Directory.GetDirectories(fullPath);

                if (dirs.Length > 0)
                    directories.AddRange(dirs);
            }
            catch { }

            directories.ForEach(directoryPath =>
            {
                var subItem = new TreeViewItem()
                {
                    Header = GetFileFolderName(directoryPath),
                    Tag = directoryPath
                };

                subItem.Items.Add(null);
                subItem.Expanded += Folder_Expanded;
                item.Items.Add(subItem);
            });


            var files = new List<string>();
            try
            {
                var fs = Directory.GetFiles(fullPath);

                if (fs.Length > 0)
                    files.AddRange(fs);
            }
            catch { }

            files.ForEach(filePath =>
            {
                var subItem = new TreeViewItem()
                {
                    Header = GetFileFolderName(filePath),
                    Tag = filePath
                };
                item.Items.Add(subItem);
            });

        }

        public static string GetFileFolderName(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            var normalizedPath = path.Replace('/', '\\');

            var lastIndex = normalizedPath.LastIndexOf('\\');

            if (lastIndex <= 0)
                return path;

            return path.Substring(startIndex: lastIndex + 1);
        }



        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }





        public static void PlotMaker()
        {
            // With sample data
            var plotWindow = new PlotWindow();
            plotWindow.Show();

            // With your own data
            //var data = new List<(double, double)> { (1, 2), (2, 4), (3, 6) };
            //var plotWindow = new PlotWindow(data);
            //plotWindow.Show();
        }




    }
}

