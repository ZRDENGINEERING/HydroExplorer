using HydroExplorer.Helpers;
using HydroExplorer.Utils;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace HydroExplorer.View
{
    // ── Tree node model ──────────────────────────────────────────────────────
    public class TreeNodeInfo : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

        public string Path { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public bool IsFolder { get; init; }
        public bool IsCheckable { get; init; }

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set { _isChecked = value; OnPropertyChanged(); }
        }
    }

    // ── FilteredTreeView ─────────────────────────────────────────────────────
    public partial class FilteredTreeView : UserControl
    {
        public string FileExtensionFilter { get; set; } = ".prj";

        public Func<UserSettings, string>? TargetPathResolver { get; set; }

        private readonly object? _dummyNode = null;
        private readonly IUserSettingsRepo _settingsRepo;
        private bool _initialized = false;

        public FilteredTreeView()
        {
            InitializeComponent();
            _settingsRepo = App.ServiceProvider.GetRequiredService<IUserSettingsRepo>();

            EventBus.AppLoaded += OnAppLoaded;

            EventBus.ProjPathChanged += async path =>
            {
                var settings = await _settingsRepo.GetSettings();
                string targetPath = ResolveTargetPath(settings);
                if (string.IsNullOrEmpty(targetPath)) return;

                await Dispatcher.InvokeAsync(async () =>
                {
                    CollapseAll();
                    await ExpandToPath(targetPath);
                });
            };

            EventBus.RecentProjectSelected += async path =>
            {
                var settings = await _settingsRepo.GetSettingsFresh();
                string targetPath = string.Empty;

                if (settings.Projects.TryGetValue(path, out var proj) &&
                    !string.IsNullOrEmpty(proj.ProjRoot))
                    targetPath = proj.ProjRoot;
                else
                    targetPath = ResolveTargetPath(settings);

                if (string.IsNullOrEmpty(targetPath)) return;

                await Dispatcher.InvokeAsync(async () =>
                {
                    CollapseAll();
                    await ExpandToPath(targetPath);
                });
            };
        }

        // ── Init ─────────────────────────────────────────────────────────────

        private void CollapseAll()
        {
            foreach (var item in foldersItem.Items.OfType<TreeViewItem>())
                CollapseItem(item);
        }

        private void CollapseItem(TreeViewItem item)
        {
            item.IsExpanded = false;
            foreach (var child in item.Items.OfType<TreeViewItem>())
                CollapseItem(child);
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
                var rootInfo = new TreeNodeInfo
                {
                    Path = rootPath,
                    DisplayName = rootPath,
                    IsFolder = true,
                    IsCheckable = false
                };
                var rootItem = new TreeViewItem
                {
                    Header = rootInfo,
                    Tag = rootInfo,
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
            if (TargetPathResolver != null)
                return TargetPathResolver(settings);

            string projPath = TreeViewControl.NormalizeProjKey(settings.LastProjPath);

            if (FileExtensionFilter == ".prj" || FileExtensionFilter == ".hms")
            {
                if (!string.IsNullOrEmpty(projPath) &&
                    settings.Projects.TryGetValue(projPath, out var proj))
                    return proj.ProjRoot;

                if (settings.Projects.TryGetValue(settings.LastProjPath, out var hmsproj))
                    return hmsproj.ProjRoot;

                return Path.GetDirectoryName(projPath) ?? string.Empty;
            }

            if (FileExtensionFilter == ".shp")
            {
                if (!string.IsNullOrEmpty(projPath) &&
                    settings.Projects.TryGetValue(projPath, out var shpProj))
                {
                    if (!string.IsNullOrEmpty(shpProj.SpatialBndyPath))
                        return Path.GetDirectoryName(shpProj.SpatialBndyPath) ?? string.Empty;

                    if (!string.IsNullOrEmpty(shpProj.SpatialXsPath))
                        return Path.GetDirectoryName(shpProj.SpatialXsPath) ?? string.Empty;
                }

                var latest = settings.ShpPaths
                    .OrderByDescending(kv => kv.Value.LastOpened)
                    .FirstOrDefault();
                return Path.GetDirectoryName(latest.Key) ?? string.Empty;
            }

            return string.Empty;
        }

        // ── Tree item helpers ─────────────────────────────────────────────────

        private TreeViewItem MakeFileItem(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            bool checkable = ext is ".prj" or ".hms" or ".shp";

            var info = new TreeNodeInfo
            {
                Path = filePath,
                DisplayName = TreeViewControl.GetFileFolderName(filePath),
                IsFolder = false,
                IsCheckable = checkable,
                IsChecked = IsCurrentlyActive(filePath)
            };

            return new TreeViewItem { Header = info, Tag = info };
        }

        private TreeViewItem MakeFolderItem(string dirPath)
        {
            var info = new TreeNodeInfo
            {
                Path = dirPath,
                DisplayName = TreeViewControl.GetFileFolderName(dirPath),
                IsFolder = true,
                IsCheckable = false
            };
            var item = new TreeViewItem { Header = info, Tag = info };
            item.Items.Add(_dummyNode);
            item.Expanded += Folder_Expanded;
            return item;
        }

        /// <summary>
        /// Checks whether this file path is the currently active selection
        /// for its type, for pre-checking the checkbox on tree expansion.
        /// Uses a synchronous settings read to avoid async complexity in
        /// the tree-building path — acceptable since it reads a cached copy.
        /// </summary>
        private bool IsCurrentlyActive(string filePath)
        {
            try
            {
                var settings = _settingsRepo.GetSettings().GetAwaiter().GetResult();
                string projPath = TreeViewControl.NormalizeProjKey(settings.LastProjPath);
                var ext = Path.GetExtension(filePath).ToLowerInvariant();

                return ext switch
                {
                    ".prj" => string.Equals(filePath, projPath, StringComparison.OrdinalIgnoreCase),
                    ".hms" => settings.Projects.TryGetValue(projPath, out var p) &&
                              string.Equals(filePath, p.HmsPath, StringComparison.OrdinalIgnoreCase),
                    ".shp" => settings.Projects.TryGetValue(projPath, out var sp) &&
                              (string.Equals(filePath, sp.SpatialBndyPath, StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(filePath, sp.SpatialXsPath, StringComparison.OrdinalIgnoreCase)),
                    _ => false
                };
            }
            catch { return false; }
        }

        // ── Tree expansion ────────────────────────────────────────────────────

        private void Folder_Expanded(object sender, RoutedEventArgs e)
        {
            if (sender is not TreeViewItem item) return;
            if (item.Items.Count != 1 || item.Items[0] != null) return;

            item.Items.Clear();

            string fullPath = item.Tag is TreeNodeInfo n ? n.Path : item.Tag?.ToString() ?? string.Empty;
            bool isRoot = fullPath.TrimEnd('\\').Equals(@"C:\Temp", StringComparison.OrdinalIgnoreCase);

            if (FileExtensionFilter == ".hms" || FileExtensionFilter == ".prj")
            {
                if (isRoot)
                {
                    try
                    {
                        foreach (var dir in Directory.GetDirectories(fullPath))
                        {
                            if (!DirectoryContainsFilter(dir)) continue;
                            item.Items.Add(MakeFolderItem(dir));
                        }
                    }
                    catch { }
                    return;
                }

                try
                {
                    var files = FileExtensionFilter == ".prj"
                        ? Directory.GetFiles(fullPath, "*.prj", SearchOption.AllDirectories)
                            .Where(f => IsHecRasProjectFile(f))
                        : FileExtensionFilter == ".hms"
                            ? Directory.GetFiles(fullPath, "*.hms", SearchOption.AllDirectories)
                            : Directory.GetFiles(fullPath, "*.run", SearchOption.AllDirectories);

                    foreach (var filePath in files)
                        item.Items.Add(MakeFileItem(filePath));
                }
                catch { }
                return;
            }

            // .shp — keep folder hierarchy
            bool hasFilesHere = false;
            try
            {
                hasFilesHere = Directory.GetFiles(fullPath)
                    .Any(f => f.EndsWith(FileExtensionFilter, StringComparison.OrdinalIgnoreCase));
            }
            catch { }

            if (!hasFilesHere)
            {
                try
                {
                    foreach (var dir in Directory.GetDirectories(fullPath))
                    {
                        if (!DirectoryContainsFilter(dir)) continue;
                        item.Items.Add(MakeFolderItem(dir));
                    }
                }
                catch { }
            }

            try
            {
                foreach (var filePath in Directory.GetFiles(fullPath))
                {
                    if (!Path.GetFileName(filePath).EndsWith(FileExtensionFilter,
                        StringComparison.OrdinalIgnoreCase)) continue;
                    item.Items.Add(MakeFileItem(filePath));
                }
            }
            catch { }
        }

        // ── Selection + checkbox handlers ─────────────────────────────────────

        public async void FoldersItem_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (sender is not TreeView) return;
            if (e.NewValue is not TreeViewItem selectedItem) return;
            if (selectedItem.Tag is not TreeNodeInfo info) return;
            if (info.IsFolder) return;

            string path = info.Path;
            var ext = Path.GetExtension(path).ToLowerInvariant();

            switch (ext)
            {
                case ".prj":
                    System.Diagnostics.Debug.WriteLine($"[FilteredTreeView .prj] path={path}");
                    if (IsHecRasProjectFile(path))
                    {
                        EventBus.PublishProjPath(path);
                        await SaveProjPathAsync(path);
                        SetSingleActive(path);
                    }
                    break;
                
                case ".hms":
                    System.Diagnostics.Debug.WriteLine($"[FilteredTreeView .hms] path={path}");
                    await SaveHmsPathAsync(path);
                    SetSingleActive(path);
                    break;

                case ".run":
                    System.Diagnostics.Debug.WriteLine($"[FilteredTreeView .run] path={path}");
                    await SaveRunPathAsync(path);
                    SetSingleActive(path);
                    break;

                case ".shp":
                    // .shp checked state toggled via checkbox, not tree selection
                    EventBus.PublishShpPath(path);
                    break;
            }
        }

        public void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb) return;
            if (cb.DataContext is not TreeNodeInfo info) return;

            var ext = Path.GetExtension(info.Path).ToLowerInvariant();

            if (ext is ".prj" or ".hms")
                SetSingleActive(info.Path);

            _ = OnFileCheckedAsync(info.Path, ext, true);
        }

        public void CheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb) return;
            if (cb.DataContext is not TreeNodeInfo info) return;

            var ext = Path.GetExtension(info.Path).ToLowerInvariant();
            _ = OnFileCheckedAsync(info.Path, ext, false);
        }

        private async Task OnFileCheckedAsync(string path, string ext, bool isChecked)
        {
            switch (ext)
            {
                case ".prj":
                    if (isChecked && IsHecRasProjectFile(path))
                    {
                        EventBus.PublishProjPath(path);
                        await SaveProjPathAsync(path);
                    }
                    break;

                case ".hms":
                    if (isChecked)
                        await SaveHmsPathAsync(path);
                    break;

                case ".run":
                    if (isChecked)
                        await SaveRunPathAsync(path);
                    break;

                case ".shp":
                    // Toggle map layer — publish so MapView/MapOverView can react
                    EventBus.PublishShpPath(path);
                    break;
            }
        }

        /// <summary>
        /// For single-select types (.prj, .run) — walks all visible tree items
        /// and sets IsChecked = true only for the active path, false for all others
        /// of the same extension, keeping the visual indicator in sync.
        /// </summary>
        private void SetSingleActive(string activePath)
        {
            var ext = Path.GetExtension(activePath).ToLowerInvariant();
            SetSingleActiveInItems(foldersItem.Items, activePath, ext);
        }

        private void SetSingleActiveInItems(ItemCollection items, string activePath, string ext)
        {
            foreach (TreeViewItem item in items.OfType<TreeViewItem>())
            {
                if (item.Tag is TreeNodeInfo info && !info.IsFolder &&
                    Path.GetExtension(info.Path).ToLowerInvariant() == ext)
                {
                    info.IsChecked = string.Equals(info.Path, activePath,
                        StringComparison.OrdinalIgnoreCase);
                }

                if (item.IsExpanded)
                    SetSingleActiveInItems(item.Items, activePath, ext);
            }
        }

        // ── Persistence helpers ───────────────────────────────────────────────

        private async Task SaveHmsPathAsync(string runPath)
        {
            var settings = await _settingsRepo.GetSettingsFresh();
            settings.HmsProjects[runPath] = DateTime.Now;

            var current = new DirectoryInfo(Path.GetDirectoryName(runPath) ?? string.Empty);
            while (current?.Parent != null &&
                   !current.Parent.FullName.Equals(@"C:\Temp", StringComparison.OrdinalIgnoreCase))
                current = current.Parent;

            string projRoot = current?.FullName ?? string.Empty;

            string existingKey = settings.Projects
                .Where(kv => !string.IsNullOrEmpty(kv.Value.ProjRoot)
                    && kv.Value.ProjRoot.Equals(projRoot, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key)
                .FirstOrDefault() ?? string.Empty;

            string activeKey = !string.IsNullOrEmpty(existingKey) ? existingKey : runPath;

            if (!settings.Projects.TryGetValue(activeKey, out var proj))
                proj = settings.Projects[activeKey] = new ProjectSettings();

            proj.ProjName = current?.Name ?? string.Empty;
            proj.ProjRoot = projRoot;
            proj.HmsPath = runPath;
            proj.LastOpened = DateTime.Now;

            settings.LastProjPath = activeKey;
            settings.ProjPath = activeKey;

            await _settingsRepo.SaveSettings(settings);

            EventBus.PublishRunPath(runPath);
            EventBus.PublishProjPathChanged(activeKey);
        }

        private async Task SaveRunPathAsync(string runPath)
        {
            var settings = await _settingsRepo.GetSettingsFresh();
            settings.HmsProjects[runPath] = DateTime.Now;

            var current = new DirectoryInfo(Path.GetDirectoryName(runPath) ?? string.Empty);
            while (current?.Parent != null &&
                   !current.Parent.FullName.Equals(@"C:\Temp", StringComparison.OrdinalIgnoreCase))
                current = current.Parent;

            string projRoot = current?.FullName ?? string.Empty;

            string existingKey = settings.Projects
                .Where(kv => !string.IsNullOrEmpty(kv.Value.ProjRoot)
                    && kv.Value.ProjRoot.Equals(projRoot, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key)
                .FirstOrDefault() ?? string.Empty;

            string activeKey = !string.IsNullOrEmpty(existingKey) ? existingKey : runPath;

            if (!settings.Projects.TryGetValue(activeKey, out var proj))
                proj = settings.Projects[activeKey] = new ProjectSettings();

            proj.ProjName = current?.Name ?? string.Empty;
            proj.ProjRoot = projRoot;
            proj.HmsPath = runPath;
            proj.LastOpened = DateTime.Now;

            settings.LastProjPath = activeKey;
            settings.ProjPath = activeKey;

            await _settingsRepo.SaveSettings(settings);

            EventBus.PublishRunPath(runPath);
            EventBus.PublishProjPathChanged(activeKey);
        }

        private async Task SaveProjPathAsync(string projPath)
        {
            string normalizedPath = TreeViewControl.NormalizeProjKey(projPath);
            var settings = await _settingsRepo.GetSettingsFresh();
            settings.LastProjPath = normalizedPath;
            settings.ProjPath = normalizedPath;

            var current = new DirectoryInfo(Path.GetDirectoryName(normalizedPath) ?? string.Empty);
            while (current?.Parent != null &&
                   !current.Parent.FullName.Equals(@"C:\Temp", StringComparison.OrdinalIgnoreCase))
                current = current.Parent;

            if (!settings.Projects.TryGetValue(normalizedPath, out var proj))
                proj = settings.Projects[normalizedPath] = new ProjectSettings();

            proj.ProjName = current?.Name ?? string.Empty;
            proj.ProjRoot = current?.FullName ?? string.Empty;
            proj.LastOpened = DateTime.Now;
            proj.ProjPath = normalizedPath;

            await _settingsRepo.SaveSettings(settings);
            EventBus.PublishProjPath(normalizedPath);
        }

        // ── Tree navigation ───────────────────────────────────────────────────

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

            await InitializeTreeAsync(null);

            TreeViewItem? currentItem = null;
            foreach (TreeViewItem item in foldersItem.Items)
            {
                string itemPath = item.Tag is TreeNodeInfo n ? n.Path : item.Tag?.ToString() ?? string.Empty;
                if (targetPath.StartsWith(itemPath, StringComparison.OrdinalIgnoreCase))
                {
                    currentItem = item;
                    break;
                }
            }
            if (currentItem == null) return;

            string rootPath = currentItem.Tag is TreeNodeInfo rn ? rn.Path : currentItem.Tag?.ToString() ?? string.Empty;
            string remaining = targetPath[rootPath.TrimEnd('\\').Length..].TrimStart('\\');
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
                    string childDisplay = child.Tag is TreeNodeInfo cn
                        ? cn.DisplayName
                        : child.Header?.ToString() ?? string.Empty;

                    if (childDisplay.Equals(part, StringComparison.OrdinalIgnoreCase))
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
                currentItem.IsExpanded = true;
                await Task.Delay(100);
                Application.Current.Dispatcher.Invoke(() => { },
                    System.Windows.Threading.DispatcherPriority.Render);

                currentItem.IsSelected = true;
                currentItem.BringIntoView();
            }
        }

        // ── Utilities ─────────────────────────────────────────────────────────

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

        private bool DirectoryContainsFilter(string dir)
        {
            try
            {
                if (FileExtensionFilter == ".prj")
                    return Directory.GetFiles(dir, "*.prj", SearchOption.AllDirectories)
                        .Any(f => IsHecRasProjectFile(f));

                return Directory.GetFiles(dir, "*" + FileExtensionFilter, SearchOption.AllDirectories)
                    .Length > 0;
            }
            catch { return false; }
        }
    }
}