using HydroExplorer.ViewModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;



namespace HydroExplorer.View
{
    public partial class TreeViewControl : UserControl
    {

        private object? dummyNode = null;

        public TreeViewControl()
        {
            InitializeComponent();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void NotifyPropertyChanged([CallerMemberName] string? name = null)
    => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));





        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            foreach (string s in Directory.GetLogicalDrives())
            {
                TreeViewItem item = new()
                {
                    Header = s,
                    Tag = s,
                    FontWeight = FontWeights.Normal
                };
                item.Items.Add(dummyNode);
                item.Expanded += new RoutedEventHandler(folder_Expanded);
                foldersItem.Items.Add(item);
            }
        }


        void folder_Expanded(object sender, RoutedEventArgs e)
        {
            TreeViewItem item = (TreeViewItem)sender;
            if (item.Items.Count == 1 && item.Items[0] == dummyNode)
            {
                item.Items.Clear();
                try
                {
                    foreach (string s in Directory.GetDirectories(item.Tag.ToString()))
                    {
                        TreeViewItem subitem = new()
                        {
                            Header = s.Substring(s.LastIndexOf("\\") + 1),
                            Tag = s,
                            FontWeight = FontWeights.Normal
                        };
                        subitem.Items.Add(dummyNode);
                        subitem.Expanded += new RoutedEventHandler(folder_Expanded);
                        item.Items.Add(subitem);
                    }
                }
                catch (Exception) { }
            }
        }


        public string _selectedImagePath;
        public string SelectedImagePath
        {
            get => _selectedImagePath;
            set
            {
                _selectedImagePath = value;
                NotifyPropertyChanged();
            }
        }


        public void FoldersItem_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var vm = DataContext as MainWindowViewModel;
            if (vm == null) return;

            TreeView tree = (TreeView)sender;
            TreeViewItem temp = ((TreeViewItem)tree.SelectedItem);
            if (temp == null) return;

            string path = "";
            string sep = "";

            while (true)
            {
                string header = temp.Header.ToString();
                if (header.Contains(@"\")) sep = "";
                path = header + sep + path;

                if (temp.Parent.GetType().Equals(typeof(TreeView))) break;

                temp = ((TreeViewItem)temp.Parent);
                sep = @"\";
            }
            System.Diagnostics.Debug.WriteLine($"PropertyChangedEventHandler {SelectedImagePath}\n");

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedImagePath)));
            vm.SelectedImagePath = path; // this triggers OnPropertyChanged automatically
        }
    }
}