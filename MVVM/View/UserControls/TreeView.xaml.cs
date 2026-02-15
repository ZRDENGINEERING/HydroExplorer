using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;



namespace HydroExplorer.MVVM.View.UserControls
{
    public partial class TreeView : UserControl
    {
        public TreeView()
        {
            InitializeComponent();
        }


        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            foreach (var drive in Directory.GetLogicalDrives())
            {
                var item = new TreeViewItem()
                {
                    Header = drive,
                    Tag = drive
                };

                item.Items.Add(null);
                item.Expanded += Folder_Expanded;
                FolderView.Items.Add(item);
            }
        }


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
                {
                    if (dirs.Length > 0)
                        directories.AddRange(dirs);
                }
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
            //string[] extensions = { ".jpg", ".png", ".gif" };
            string[] extensions = { ".hdf" };
            try
            {
                var fs = Directory.GetFiles(fullPath)
                    .Where(file => extensions.Any(ext => Path.GetExtension(file).Equals(ext, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                if (fs.Count > 0)
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

            return path.Substring(lastIndex);
        }



        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {

            //FolderView
            object slctdItem = e.NewValue;

            //System.Diagnostics.Debug.WriteLine($"selectedItem : {e}");
            var slctdVar = e.NewValue.ToString();
            var slctdFile = slctdVar.Split(':').ElementAt(1).Split(' ').ElementAt(0);

            System.Diagnostics.Debug.WriteLine($"slctdVar : {slctdVar}");
            //System.Diagnostics.Debug.WriteLine($"slctdVar.GetType() : {slctdVar.GetType()}");


            TreeViewItem? selectedItem = e.NewValue as TreeViewItem;
            if (selectedItem != null)
            {
                string path = GetFullPath(selectedItem);
                // Use the path (e.g., display in a TextBox)
                MessageBox.Show(path);
            }
        }


        private string GetFullPath(TreeViewItem item)
        {
            StringBuilder path = new StringBuilder();
            // Start with the current item's header
            path.Insert(0, item.Header.ToString());

            // Loop up the visual or logical tree to find parent TreeViewItems
            DependencyObject parent = VisualTreeHelper.GetParent(item); // Or LogicalTreeHelper
            while (parent != null && parent is not TreeView)
            {
                TreeViewItem? parentItem = parent as TreeViewItem;
                if (parentItem != null)
                {
                    path.Insert(0, parentItem.Header.ToString() + "\\"); // Use appropriate separator
                }
                parent = VisualTreeHelper.GetParent(parent);
            }

            System.Diagnostics.Debug.WriteLine($"path : {path}");
            System.Diagnostics.Debug.WriteLine($"parent : {parent}");

            return path.ToString();
        }






    }
}