using HydroExplorer.Core;
using HydroExplorer.MVVM.Models;
using HydroExplorer.MVVM.Behaviors;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
//using System.Windows.Forms;
using System.Windows.Media;



namespace HydroExplorer.MVVM.ViewModels
{
    class MainViewModel : BaseViewModel
    {
        public HomeViewModel HomeVM { get; set; }
        public DiscoveryViewModel DiscoveryVM { get; set; }
        public DataGridViewModel DataGridVM { get; set; }
        public MapViewModel MapVM { get; set; }

        public string Header { get; set; }
        public string Tag { get; set; }

        public ObservableCollection<TreeNodeModel> TreeItems { get; } = new();

        private readonly TreeView FolderView = new();

        
        private TreeNodeModel _selectedNode;
        public TreeNodeModel SelectedNode
        {
            get => _selectedNode;
            set
            {
                SetField(ref _selectedNode, value);
                OnPropertyChanged(nameof(SelectedDetail));

                System.Diagnostics.Debug.WriteLine($"SelectedNode SelectedNode....");
            }
        }

        //--- What shows in the TextBox ---
        public string SelectedDetail => SelectedNode != null
            ? $"{SelectedNode.Header}: {SelectedNode.Tag}"
            : "No item selected.";



        private object _currentView;

        public object CurrentView
        {
            get { return _currentView; }
            set
            {
                _currentView = value;
                OnPropertyChanged();
            }
        }

        public RelayCommand HomeViewCommand { get; set; }
        public RelayCommand DiscoveryViewCommand { get; set; }
        public RelayCommand DataGridViewCommand { get; set; }
        public RelayCommand MapViewCommand { get; set; }
        

        public MainViewModel()
        {
            HomeVM = new HomeViewModel();
            DiscoveryVM = new DiscoveryViewModel();
            DataGridVM = new DataGridViewModel();
            MapVM = new MapViewModel();

            CurrentView = HomeVM;

            HomeViewCommand = new RelayCommand(o => { CurrentView = HomeVM; }, canExecute: o => true);
            DiscoveryViewCommand = new RelayCommand(o => { CurrentView = DiscoveryVM; }, canExecute: o => true);
            DataGridViewCommand = new RelayCommand(o => { CurrentView = DataGridVM; }, canExecute: o => true);
            MapViewCommand = new RelayCommand(o => { CurrentView = MapVM; }, canExecute: o => true);

            LoadTree();
        }

        

        private void LoadTree()
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
                
                TreeItems.Add(new TreeNodeModel
                {
                    Header = drive,
                    Tag = drive
                });

                //System.Diagnostics.Debug.WriteLine($"path : {"path"}");

            }
            //OnPropertyChanged(sender, e);

            System.Diagnostics.Debug.WriteLine($"path : {TreeItems}");
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


                TreeItems.Add(new TreeNodeModel
                {
                    Header = GetFileFolderName(directoryPath),
                    Tag = directoryPath
                });
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

                TreeItems.Add(new TreeNodeModel
                {
                    Header = GetFileFolderName(filePath),
                    Tag = filePath
                });
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
            //object slctdItem = e.NewValue;
            //var slctdVar = e.NewValue.ToString();
            //var slctdFile = slctdVar.Split(':').ElementAt(1).Split(' ').ElementAt(0);

            TreeViewItem? selectedItem = e.NewValue as TreeViewItem;
            if (selectedItem != null)
            {
                string path = GetFullPath(selectedItem);

                System.Diagnostics.Debug.WriteLine($"path : {path}");
                //MessageBox.Show(path);

                //HomeView _hView = new();

                //if (DataContext is HydroExplorer.MVVM.ViewModels.MainViewModel vm && e.NewValue is System.Windows.Controls.TreeViewItem item)
                //if (DataContext is HydroExplorer.MVVM.ViewModels.MainViewModel vm && e.NewValue is System.Windows.Controls.TreeViewItem item)
                //{
                //    vm.SelectedNode = (TreeNodeHdf)e.NewValue;

                //}

                //+e.NewValue  { System.Windows.Controls.TreeViewItem Header:\TAN_Main_SG.p02.hdf Items.Count: 0}
                //object { System.Windows.Controls.TreeViewItem}

                //TreeViewSelectedItemBehavior.SetSelectedItem();

                //_hView.OnPropertyChanged(sender, e);
            }
        }



        private string GetFullPath(TreeViewItem item)
        {
            StringBuilder path = new StringBuilder();
            path.Insert(0, item.Header.ToString());

            DependencyObject parent = VisualTreeHelper.GetParent(item);
            while (parent != null && parent is not TreeViewItem)
            {
                TreeViewItem? parentItem = parent as TreeViewItem;
                if (parentItem != null)
                {
                    path.Insert(0, parentItem.Header.ToString() + "\\");
                }
                parent = VisualTreeHelper.GetParent(parent);
            }
            return path.ToString();
        }


    }



    //public event PropertyChangedEventHandler? PropertyChanged;
    //public void OnPropertyChanged([CallerMemberName] string propertyName = "")
    //{
    //    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    //    System.Diagnostics.Debug.WriteLine($"OnPropertyChanged HIT....");
    //}





}

