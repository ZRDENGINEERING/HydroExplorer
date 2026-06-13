using HydroExplorer.Helpers;
using HydroExplorer.Utils;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace HydroExplorer.View
{
    public partial class FilteredTreeView : UserControl
    {
        public string FileExtensionFilter { get; set; } = ".prj";

        // Parent pane sets this to tell the tree which path to expand to on load
        public Func<UserSettings, string>? TargetPathResolver { get; set; }

        private readonly object? _dummyNode = null;
        private readonly IUserSettingsRepo _settingsRepo;
        private bool _initialized = false;

        public FilteredTreeView()
        {
            InitializeComponent();
            _settingsRepo = App.ServiceProvider.GetRequiredService<IUserSettingsRepo>();

            EventBus.AppLoaded += OnAppLoaded;
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (_initialized) return;
            _initialized = true;
            try { await InitializeTreeAsync(null); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FilteredTreeView.Loaded error: {ex.Message}");
            }
        }

        private void OnAppLoaded(UserSettings settings)
        {
            Dispatcher.Invoke(async () =>
            {
                string targetPath = ResolveTargetPath(settings);
                if (!string.IsNullOrEmpty(targetPath))
                    await ExpandToPath(targetPath);
            });
        }

        private async Task InitializeTreeAsync(string? expandToDir)
        {
            string rootPath = @"C:\Temp\";
            if (Directory.Exists(rootPath) && foldersItem.Items.Count == 0)
            {
                var rootItem = new TreeViewItem
                {
                    Header = rootPath,
                    Tag = rootPath,
                    FontWeight = FontWeights.Normal
                };
                rootItem.Items.Add(_dummyNode);
                rootItem.Expanded += Folder_Expanded;
                foldersItem.Items.Add(rootItem);
            }

            if (!string.IsNullOrEmpty(expandToDir))
                await ExpandToPath(expandToDir);
        }

        private string ResolveTargetPath(UserSettings settings)
        {
            // Use caller-supplied resolver if provided
            if (TargetPathResolver != null)
                return TargetPathResolver(settings);

            // Default fallback by extension
            string projPath = TreeViewControl.NormalizeProjKey(settings.LastProjPath);

            if (FileExtensionFilter == ".prj" || FileExtensionFilter == ".run")
            {
                // For both hydraulics and hydrology: expand to project dir
                if (!string.IsNullOrEmpty(projPath) &&
                    settings.Projects.TryGetValue(projPath, out var proj))
                {
                    if (FileExtensionFilter == ".run" && !string.IsNullOrEmpty(proj.HmsPath))
                        return Path.GetDirectoryName(proj.HmsPath) ?? string.Empty;

                    return proj.ProjDir;
                }
                return Directory.Exists(projPath)
                    ? projPath
                    : Path.GetDirectoryName(projPath) ?? string.Empty;
            }

            if (FileExtensionFilter == ".shp")
            {
                // Expand to most recently opened shp directory
                var latest = settings.ShpPaths
                    .OrderByDescending(kv => kv.Value.LastOpened)
                    .FirstOrDefault();
                return Path.GetDirectoryName(latest.Key) ?? string.Empty;
            }

            return string.Empty;
        }

        private void Folder_Expanded(object sender, RoutedEventArgs e)
        {
            if (sender is not TreeViewItem item) return;
            if (item.Items.Count != 1 || item.Items[0] != null) return;

            item.Items.Clear();
            var fullPath = (string)item.Tag;

            try
            {
                foreach (var dir in Directory.GetDirectories(fullPath))
                {
                    var subItem = new TreeViewItem
                    {
                        Header = TreeViewControl.GetFileFolderName(dir),
                        Tag = dir
                    };
                    subItem.Items.Add(_dummyNode);
                    subItem.Expanded += Folder_Expanded;
                    item.Items.Add(subItem);
                }
            }
            catch { }

            try
            {
                foreach (var filePath in Directory.GetFiles(fullPath))
                {
                    var fileName = TreeViewControl.GetFileFolderName(filePath);
                    if (!fileName.EndsWith(FileExtensionFilter, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (FileExtensionFilter == ".prj" && !IsHecRasProjectFile(filePath))
                        continue;
                    item.Items.Add(new TreeViewItem { Header = fileName, Tag = filePath });
                }
            }
            catch { }
        }

        public void FoldersItem_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (sender is not TreeView tree) return;
            if (tree.SelectedItem is not TreeViewItem temp) return;

            string path = "";
            string sep = "";
            while (true)
            {
                string? header = temp.Header.ToString();
                if (header!.Contains('\\')) sep = "";
                path = header + sep + path;
                if (temp.Parent.GetType() == typeof(TreeView)) break;
                temp = (TreeViewItem)temp.Parent;
                sep = @"\";
            }

            var ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".prj":
                    if (IsHecRasProjectFile(path)) EventBus.PublishProjPath(path);
                    break;
                case ".run":
                    EventBus.PublishRunPath(path);
                    break;
                case ".shp":
                    EventBus.PublishShpPath(path);
                    break;
            }
        }

        private void FoldersItem_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            foreach (var item in foldersItem.Items.OfType<TreeViewItem>())
                item.Background = System.Windows.Media.Brushes.Transparent;
        }

        private void TreeViewItem_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
            => e.Handled = true;

        private async Task ExpandToPath(string targetPath)
        {
            if (string.IsNullOrEmpty(targetPath) || !Directory.Exists(targetPath)) return;

            // Ensure root is added if tree is empty
            await InitializeTreeAsync(null);

            TreeViewItem? currentItem = null;
            foreach (TreeViewItem item in foldersItem.Items)
            {
                if (targetPath.StartsWith(item.Tag?.ToString() ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase))
                {
                    currentItem = item;
                    break;
                }
            }
            if (currentItem == null) return;

            string rootTag = currentItem.Tag?.ToString() ?? string.Empty;
            string remaining = targetPath[rootTag.TrimEnd('\\').Length..].TrimStart('\\');
            var parts = remaining.Split(Path.DirectorySeparatorChar,
                StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in parts)
            {
                currentItem.IsExpanded = true;
                await Task.Delay(100);
                Application.Current.Dispatcher.Invoke(() => { },
                    System.Windows.Threading.DispatcherPriority.Render);

                TreeViewItem? next = null;
                foreach (TreeViewItem child in currentItem.Items)
                {
                    if (child.Header?.ToString()?.Equals(part,
                        StringComparison.OrdinalIgnoreCase) == true)
                    {
                        next = child;
                        break;
                    }
                }
                if (next == null) break;
                currentItem = next;
            }

            if (currentItem != null)
            {
                currentItem.IsSelected = true;
                currentItem.BringIntoView();
            }
        }

        private static bool IsHecRasProjectFile(string filePath)
        {
            try
            {
                using var reader = new StreamReader(filePath);
                return reader.ReadLine()?.Trim()
                    .StartsWith("Proj Title", StringComparison.OrdinalIgnoreCase) == true;
            }
            catch { return false; }
        }
    }
}