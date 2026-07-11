using HydroExplorer.Helpers;
using HydroExplorer.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileSystemGlobbing;
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

        private FileSystemWatcher? _watcher;
        private CancellationTokenSource? _refreshCts;

        // FileSystemWatcher raises events on ThreadPool threads, not the UI
        // thread. Setting up a project with multiple files landing in the
        // watched folder close together (e.g. a .shp write drops .shp/.shx/
        // .dbf/.prj together, or a .hms + .shp pair get added at once) can
        // fire several Created events on different threads within
        // milliseconds of each other. OnFileSystemChanged used to read,
        // Cancel(), and reassign _refreshCts with no synchronization —
        // two threads could race: one disposes the CancellationTokenSource
        // while another, still holding the same now-disposed reference,
        // calls .Cancel() on it, throwing ObjectDisposedException. This lock
        // serializes every touch of _refreshCts (including Unloaded's
        // cleanup) so only one thread can read/cancel/dispose/reassign it
        // at a time.
        private readonly object _refreshCtsLock = new();

        public FilteredTreeView()
        {
            InitializeComponent();

            _settingsRepo = App.ServiceProvider.GetRequiredService<IUserSettingsRepo>();

            EventBus.AppLoaded += OnAppLoaded;

            EventBus.ProjPathChanged += async path =>
            {
                var settings = await _settingsRepo.GetSettings();
                string targetPath = ResolveTargetPath(settings);

                await Dispatcher.InvokeAsync(async () =>
                {
                    string normalizedPath = PathHelpers.NormalizeProjKey(settings.LastProjPath);

                    if (FileExtensionFilter == ".prj" && !string.IsNullOrEmpty(normalizedPath))
                        SetSingleActive(normalizedPath);
                    else if (FileExtensionFilter == ".hms" &&
                             settings.Projects.TryGetValue(normalizedPath, out var proj) &&
                             !string.IsNullOrEmpty(proj.HmsPath))
                        SetSingleActive(proj.HmsPath);

                    if (!string.IsNullOrEmpty(targetPath))
                    {
                        CollapseAll();
                        await ExpandToPath(targetPath);
                    }
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

                await Dispatcher.InvokeAsync(async () =>
                {
                    string normalizedPath = PathHelpers.NormalizeProjKey(settings.LastProjPath);

                    if (FileExtensionFilter == ".prj" && !string.IsNullOrEmpty(normalizedPath))
                        SetSingleActive(normalizedPath);
                    else if (FileExtensionFilter == ".hms" &&
                             settings.Projects.TryGetValue(normalizedPath, out var activeProj) &&
                             !string.IsNullOrEmpty(activeProj.HmsPath))
                        SetSingleActive(activeProj.HmsPath);

                    if (!string.IsNullOrEmpty(targetPath))
                    {
                        CollapseAll();
                        await ExpandToPath(targetPath);
                    }
                });
            };




            Unloaded += (s, e) =>
            {
                _watcher?.Dispose();

                lock (_refreshCtsLock)
                {
                    _refreshCts?.Cancel();
                    _refreshCts?.Dispose();
                    _refreshCts = null;
                }
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

                StartWatcher(rootPath);
            }

            if (!string.IsNullOrEmpty(expandToDir))
                await ExpandToPath(expandToDir);
        }



        private void StartWatcher(string rootPath)
        {
            _watcher?.Dispose();

            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath)) return;

            _watcher = new FileSystemWatcher(rootPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnFileSystemChanged;
            _watcher.Deleted += OnFileSystemChanged;
            _watcher.Renamed += OnFileSystemChanged;
        }

        private async void OnFileSystemChanged(object sender, FileSystemEventArgs e)
        {
            //    System.Diagnostics.Debug.WriteLine($"FilteredTreeView ({FileExtensionFilter}): FileSystemEvent {e.ChangeType} '{e.FullPath}'" +
            //(e is RenamedEventArgs re ? $" (was '{re.OldFullPath}')" : ""));

            CancellationToken token;
            lock (_refreshCtsLock)
            {
                _refreshCts?.Cancel();
                _refreshCts?.Dispose();
                _refreshCts = new CancellationTokenSource();
                token = _refreshCts.Token;
            }

            try
            {
                await Task.Delay(500, token);
                await Dispatcher.InvokeAsync(RefreshTree);
                //System.Diagnostics.Debug.WriteLine($"FilteredTreeView ({FileExtensionFilter}): RefreshTree completed");

            }
            catch (OperationCanceledException) { }
        }

        private void RefreshTree()
        {
            foreach (TreeViewItem root in foldersItem.Items.OfType<TreeViewItem>())
                RefreshExpandedNode(root);
        }

        /// <summary>
        /// Diffs an expanded node's children against the current filesystem state,
        /// adding/removing items as needed. Only touches already-expanded nodes —
        /// collapsed nodes will re-scan naturally on next expand via Folder_Expanded.
        /// </summary>
        private void RefreshExpandedNode(TreeViewItem item)
        {
            if (item.Tag is not TreeNodeInfo info) return;

            // Refresh any node that's already been populated (real children present,
            // not just the lazy-load dummy placeholder) — not just currently-expanded
            // ones. Otherwise a collapsed-but-previously-populated folder shows stale
            // cached items the next time it's expanded, since Folder_Expanded's guard
            // only rebuilds when it sees the dummy null placeholder.
            bool isPopulated = item.Items.Count != 1 || item.Items[0] != null;
            if (!isPopulated) return;

            string fullPath = info.Path;
            if (!Directory.Exists(fullPath)) return;

            bool isRoot = fullPath.TrimEnd('\\').Equals(@"C:\Temp", StringComparison.OrdinalIgnoreCase);

            if (FileExtensionFilter is ".run" or ".prj")
                RefreshPrjRunNode(item, fullPath, isRoot);
            else
                RefreshShpNode(item, fullPath);

            // Recurse into all populated children, regardless of expand state
            foreach (TreeViewItem child in item.Items.OfType<TreeViewItem>())
                RefreshExpandedNode(child);
        }

        private void RefreshPrjRunNode(TreeViewItem item, string fullPath, bool isRoot)
        {
            if (isRoot)
            {
                var existingDirs = item.Items.OfType<TreeViewItem>()
                    .Where(i => i.Tag is TreeNodeInfo n && n.IsFolder)
                    .ToDictionary(i => ((TreeNodeInfo)i.Tag).Path, i => i, StringComparer.OrdinalIgnoreCase);

                var currentDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (var dir in Directory.GetDirectories(fullPath))
                        if (DirectoryContainsFilter(dir))
                            currentDirs.Add(dir);
                }
                catch { return; }

                foreach (var dir in currentDirs.Where(d => !existingDirs.ContainsKey(d)))
                    item.Items.Add(MakeFolderItem(dir));

                foreach (var (path, node) in existingDirs.Where(kv => !currentDirs.Contains(kv.Key)))
                    item.Items.Remove(node);

                return;
            }

            var existingFiles = item.Items.OfType<TreeViewItem>()
                .Where(i => i.Tag is TreeNodeInfo n && !n.IsFolder)
                .ToDictionary(i => ((TreeNodeInfo)i.Tag).Path, i => i, StringComparer.OrdinalIgnoreCase);

            HashSet<string> currentFiles;
            try
            {
                currentFiles = FileExtensionFilter == ".prj"
                    ? new HashSet<string>(
                        Directory.GetFiles(fullPath, "*.prj", SearchOption.AllDirectories)
                            .Where(f => HecRasPrjReader.IsHecRasProjectFile(f)),
                        StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(
                        Directory.GetFiles(fullPath, "*.hms", SearchOption.AllDirectories),
                        StringComparer.OrdinalIgnoreCase);
            }
            catch { return; }

            foreach (var file in currentFiles.Where(f => !existingFiles.ContainsKey(f)))
                item.Items.Add(MakeFileItem(file));

            foreach (var (path, node) in existingFiles.Where(kv => !currentFiles.Contains(kv.Key)))
                item.Items.Remove(node);
        }

        private void RefreshShpNode(TreeViewItem item, string fullPath)
        {
            var existingDirs = item.Items.OfType<TreeViewItem>()
                .Where(i => i.Tag is TreeNodeInfo n && n.IsFolder)
                .ToDictionary(i => ((TreeNodeInfo)i.Tag).Path, i => i, StringComparer.OrdinalIgnoreCase);

            var existingFiles = item.Items.OfType<TreeViewItem>()
                .Where(i => i.Tag is TreeNodeInfo n && !n.IsFolder)
                .ToDictionary(i => ((TreeNodeInfo)i.Tag).Path, i => i, StringComparer.OrdinalIgnoreCase);

            HashSet<string> currentDirs = [];
            HashSet<string> currentFiles = [];

            try
            {
                bool hasFilesHere = Directory.GetFiles(fullPath)
                    .Any(f => f.EndsWith(FileExtensionFilter, StringComparison.OrdinalIgnoreCase));

                if (!hasFilesHere)
                {
                    foreach (var dir in Directory.GetDirectories(fullPath))
                        if (DirectoryContainsFilter(dir))
                            currentDirs.Add(dir);
                }

                foreach (var file in Directory.GetFiles(fullPath))
                    if (Path.GetFileName(file).EndsWith(FileExtensionFilter, StringComparison.OrdinalIgnoreCase))
                        currentFiles.Add(file);
            }
            catch { return; }

            foreach (var dir in currentDirs.Where(d => !existingDirs.ContainsKey(d)))
                item.Items.Add(MakeFolderItem(dir));
            foreach (var (path, node) in existingDirs.Where(kv => !currentDirs.Contains(kv.Key)))
                item.Items.Remove(node);

            foreach (var file in currentFiles.Where(f => !existingFiles.ContainsKey(f)))
                item.Items.Add(MakeFileItem(file));
            foreach (var (path, node) in existingFiles.Where(kv => !currentFiles.Contains(kv.Key)))
                item.Items.Remove(node);
        }




        private string ResolveTargetPath(UserSettings settings)
        {
            if (TargetPathResolver != null)
                return TargetPathResolver(settings);

            string projPath = PathHelpers.NormalizeProjKey(settings.LastProjPath);

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
                    if (!string.IsNullOrEmpty(shpProj.SpatialRiverPath))
                        return Path.GetDirectoryName(shpProj.SpatialRiverPath) ?? string.Empty;

                    string dir = Path.GetDirectoryName(projPath) ?? string.Empty;
                    string spatialGuess = Path.Combine(dir, "Spatial");
                    if (Directory.Exists(spatialGuess)) return spatialGuess;
                    if (Directory.Exists(dir)) return dir;
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
            bool checkable = ext is ".prj" or ".hms";

            var info = new TreeNodeInfo
            {
                Path = filePath,
                DisplayName = PathHelpers.GetFileFolderName(filePath),
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
                DisplayName = PathHelpers.GetFileFolderName(dirPath),
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
                string projPath = PathHelpers.NormalizeProjKey(settings.LastProjPath);
                var ext = Path.GetExtension(filePath).ToLowerInvariant();

                return ext switch
                {
                    ".prj" => string.Equals(filePath, projPath, StringComparison.OrdinalIgnoreCase),
                    ".hms" => settings.Projects.TryGetValue(projPath, out var p) &&
                              string.Equals(filePath, p.HmsPath, StringComparison.OrdinalIgnoreCase),
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
                            .Where(f => HecRasPrjReader.IsHecRasProjectFile(f))
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
                    //System.Diagnostics.Debug.WriteLine($"[FilteredTreeView .prj] path={path}");
                    if (HecRasPrjReader.IsHecRasProjectFile(path))
                    {
                        // SaveProjPathAsync publishes EventBus.ProjPathSelected itself
                        // (with the normalized path) at the end — don't also publish
                        // here, or every .prj selection fires ProjPathSelected twice
                        // for every subscriber (HydraulicsPaneView, TabInfoViewModel, etc.).
                        await SaveProjPathAsync(path);
                        SetSingleActive(path);
                    }
                    break;

                case ".hms":
                    //System.Diagnostics.Debug.WriteLine($"[FilteredTreeView .hms] path={path}");
                    await SaveHmsPathAsync(path);
                    SetSingleActive(path);
                    break;

                case ".run":
                    //System.Diagnostics.Debug.WriteLine($"[FilteredTreeView .run] path={path}");
                    await SaveHmsPathAsync(path);
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
                    if (isChecked && HecRasPrjReader.IsHecRasProjectFile(path))
                    {
                        // Same fix as FoldersItem_SelectedItemChanged — SaveProjPathAsync
                        // already publishes ProjPathSelected, so don't publish twice here.
                        await SaveProjPathAsync(path);
                    }
                    break;

                case ".hms":
                    if (isChecked)
                        await SaveHmsPathAsync(path);
                    break;

                case ".run":
                    if (isChecked)
                        await SaveHmsPathAsync(path);
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

                bool isPopulated = item.Items.Count != 1 || item.Items[0] != null;
                if (isPopulated)
                    SetSingleActiveInItems(item.Items, activePath, ext);
            }
        }

        // ── Persistence helpers ───────────────────────────────────────────────

        private async Task SaveHmsPath(string path)
        {
            try
            {
                var settings = await _settingsRepo.GetSettingsFresh();

                var current = new DirectoryInfo(Path.GetDirectoryName(path) ?? string.Empty);
                while (current?.Parent != null &&
                       !current.Parent.FullName.Equals(@"C:\Temp", StringComparison.OrdinalIgnoreCase))
                    current = current.Parent;

                string projRoot = current?.FullName ?? string.Empty;

                string existingKey = settings.Projects
                    .Where(kv => !string.IsNullOrEmpty(kv.Value.ProjRoot)
                        && kv.Value.ProjRoot.Equals(projRoot, StringComparison.OrdinalIgnoreCase))
                    .Select(kv => kv.Key)
                    .FirstOrDefault() ?? string.Empty;

                string activeKey = !string.IsNullOrEmpty(existingKey) ? existingKey : path;

                if (!settings.Projects.TryGetValue(activeKey, out var proj))
                {
                    proj = settings.Projects[activeKey] = new ProjectSettings();
                    proj.OpenOrder = settings.Projects.Count;
                }

                proj.ProjName = current?.Name ?? string.Empty;
                proj.ProjRoot = projRoot;
                proj.HmsPath = path;
                proj.LastOpened = DateTime.Now;

                settings.LastProjPath = activeKey;
                settings.ProjPath = activeKey;
                settings.ProjName = proj.ProjName;

                await _settingsRepo.SaveSettings(settings);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HydrologyPaneView.SaveHmsPath error: {ex.Message}");
            }
        }

        private async Task SaveHmsPathAsync(string runPath)
        {
            var settings = await _settingsRepo.GetSettingsFresh();

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
            {
                proj = settings.Projects[activeKey] = new ProjectSettings();
                proj.OpenOrder = settings.Projects.Count;
            }

            proj.ProjName = current?.Name ?? string.Empty;
            proj.ProjRoot = projRoot;
            proj.HmsPath = runPath;
            proj.LastOpened = DateTime.Now;

            settings.LastProjPath = activeKey;
            settings.ProjPath = activeKey;
            settings.ProjName = proj.ProjName;

            await _settingsRepo.SaveSettings(settings);

            EventBus.PublishRunPath(runPath);
            EventBus.PublishProjPathChanged(activeKey);
        }

        private async Task SaveProjPathAsync(string projPath)
        {
            string normalizedPath = PathHelpers.NormalizeProjKey(projPath);
            var settings = await _settingsRepo.GetSettingsFresh();

            var current = new DirectoryInfo(Path.GetDirectoryName(normalizedPath) ?? string.Empty);
            while (current?.Parent != null &&
                   !current.Parent.FullName.Equals(@"C:\Temp", StringComparison.OrdinalIgnoreCase))
                current = current.Parent;

            string newProjRoot = current?.FullName ?? string.Empty;

            ProjectSettings proj;

            // If the currently active project shares this ProjRoot, this is the same
            // logical project — reuse its existing ProjectSettings under the new key
            // instead of creating a second entry. HMS linkage, geometry paths, gage
            // data, and Omega inputs are ProjRoot/location-scoped and carry forward;
            // only the fields specific to the OLD .prj's HEC-RAS model/results reset,
            // since a different .prj means different plans/HDF output.
            if (!string.IsNullOrEmpty(settings.LastProjPath) &&
                !settings.LastProjPath.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase) &&
                settings.Projects.TryGetValue(settings.LastProjPath, out var activeProj) &&
                activeProj.ProjRoot.Equals(newProjRoot, StringComparison.OrdinalIgnoreCase))
            {
                proj = activeProj;
                settings.Projects.Remove(settings.LastProjPath);
                settings.Projects[normalizedPath] = proj;

                // Reset only what's tied to the specific .prj/model file — a
                // different .prj means different plans and HDF results even if
                // names happen to collide.
                proj.HdfPathA = string.Empty;
                proj.HdfPathB = string.Empty;
                proj.PlanNameA = string.Empty;
                proj.PlanNameB = string.Empty;
                proj.ProName = string.Empty;
                proj.SelectedReaches = [];
                proj.SourceEpsg = null;
            }
            else if (!settings.Projects.TryGetValue(normalizedPath, out var existing))
            {
                proj = settings.Projects[normalizedPath] = new ProjectSettings();
                proj.OpenOrder = settings.Projects.Count;
            }
            else
            {
                proj = existing;
            }

            proj.ProjName = current?.Name ?? string.Empty;
            proj.ProjRoot = newProjRoot;
            proj.LastOpened = DateTime.Now;
            proj.ProjPath = normalizedPath;

            settings.LastProjPath = normalizedPath;
            settings.ProjPath = normalizedPath;
            settings.ProjName = proj.ProjName;

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

        private bool DirectoryContainsFilter(string dir)
        {
            try
            {
                if (FileExtensionFilter == ".prj")
                    return Directory.GetFiles(dir, "*.prj", SearchOption.AllDirectories)
                        .Any(f => HecRasPrjReader.IsHecRasProjectFile(f));

                return Directory.GetFiles(dir, "*" + FileExtensionFilter, SearchOption.AllDirectories)
                    .Length > 0;
            }
            catch { return false; }
        }
    }
}