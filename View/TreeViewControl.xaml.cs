//using BruTile.Wmts.Generated;
using HydroExplorer.Helpers;
using HydroExplorer.Utils;
using HydroExplorer.ViewModel;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;



namespace HydroExplorer.View
{
    public partial class TreeViewControl : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private readonly IUserSettingsRepo _settingsRepo;
        protected void NotifyPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

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

        UserSettings settings = new();

        private readonly object? dummyNode = null;
        private bool _isLoading = false;
        string? projDir = string.Empty;
        string? projPath = string.Empty;

        private DateTime _lastSaveRequest = DateTime.MinValue;




        public TreeViewControl()
        {
            InitializeComponent();

            _settingsRepo = App.ServiceProvider.GetRequiredService<IUserSettingsRepo>();

            EventBus.HdfFileASelected += (profiles, planName) =>
            {
                cboxProfiles.ItemsSource = profiles;
                txtBoxPlanNameA.Text = planName;
            };

            EventBus.HdfFileBSelected += (profiles, planName) =>
            {
                cboxProfiles.ItemsSource = profiles;
                txtBoxPlanNameB.Text = planName;
            };

            EventBus.ProjPathSelected += async path =>
            {
                try
                {
                    txtBoxProjPath.Text = path;
                    await OnProjPathChanged(path);
                }
                catch (OperationCanceledException)
                {
                    System.Diagnostics.Debug.WriteLine("ProjPathSelected cancelled.");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ProjPathSelected error: {ex.Message}");
                }
            };

            EventBus.RunPathSelected += async path =>
            {
                try
                {
                    txtBoxHmsPath.Text = path;
                    await SaveSettings();
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"RunPathSelected error: {ex.Message}");
                }
            };

            cboxProfiles.SelectionChanged += async (s, e) =>
            {
                try
                {
                    if (cboxProfiles.SelectedItem is string selectedProfile)
                    {
                        cboxProfiles.Text = selectedProfile;
                        await SaveSettings();
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"cboxProfiles error: {ex.Message}");
                }
            };

            cboxHdfPathA.SelectionChanged += async (s, e) =>
            {
                if (_isLoading) return;
                try
                {
                    if (cboxHdfPathA.SelectedItem is string selectedHdf)
                    {
                        var allFiles = cboxHdfPathA.ItemsSource as List<string> ?? [];
                        UpdateHdfPathB(allFiles, selectedHdf);
                        var profiles = HecRasHdfReader.GetProfileNames(selectedHdf);
                        var planName = HecRasHdfReader.GetPlanName(selectedHdf);
                        cboxProfiles.ItemsSource = profiles;
                        txtBoxPlanNameA.Text = planName;
                        await SaveSettings();

                        EventBus.PublishHdfPathChanged();
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"cboxHdfPathA error: {ex.Message}");
                }
            };

            cboxHdfPathB.SelectionChanged += async (s, e) =>
            {
                if (_isLoading) return;
                try
                {
                    if (cboxHdfPathB.SelectedItem is string selectedHdf)
                    {
                        var profiles = HecRasHdfReader.GetProfileNames(selectedHdf);
                        var planName = HecRasHdfReader.GetPlanName(selectedHdf);
                        cboxProfiles.ItemsSource = profiles;
                        txtBoxPlanNameB.Text = planName;
                        await SaveSettings();
                        EventBus.PublishHdfPathChanged();
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"cboxHdfPathB error: {ex.Message}");
                }
            };
        }


        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await InitializeAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UserControl_Loaded error: {ex.Message}");
            }
        }


        private void Folder_Expanded(object sender, RoutedEventArgs e)
        {
            TreeViewItem item = (TreeViewItem)sender;
            if (item.Items.Count != 1 || item.Items[0] != null)
                return;
            item.Items.Clear();
            var fullPath = (string)item.Tag;
            List<string> directories = [];

            try
            {
                var dirs = Directory.GetDirectories(fullPath);

                if (dirs.Length > 0)
                    directories.AddRange(dirs);
            }
            catch { }

            directories.ForEach(directoryPath =>
            {
                TreeViewItem subItem = new()
                {
                    Header = GetFileFolderName(directoryPath),
                    Tag = directoryPath
                };

                subItem.Items.Add(null);
                subItem.Expanded += Folder_Expanded;
                item.Items.Add(subItem);
            });

            List<string> files = [];
            try
            {
                var fs = Directory.GetFiles(fullPath);

                if (fs.Length > 0)
                    files.AddRange(fs);
            }
            catch { }

            files.ForEach(filePath =>
            {
                var fileName = GetFileFolderName(filePath);
                bool isRun = fileName.EndsWith(".run", StringComparison.OrdinalIgnoreCase);
                bool isPrj = fileName.EndsWith(".prj", StringComparison.OrdinalIgnoreCase);

                if (!isPrj && !isRun) return;

                TreeViewItem subItem = new()
                {
                    Header = fileName,
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
            {
                return string.Empty;
            }
            else
            {
                return path[(lastIndex + 1)..];
            }
        }


        public void FoldersItem_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is not MainWindowViewModel vm) return;

            TreeView tree = (TreeView)sender;
            TreeViewItem temp = ((TreeViewItem)tree.SelectedItem);
            if (temp == null) return;

            string path = "";
            string sep = "";
            while (true)
            {
                string? header = temp.Header.ToString();
                if (header!.Contains('\\')) sep = "";
                path = header + sep + path;

                if (temp.Parent.GetType().Equals(typeof(TreeView))) break;

                temp = ((TreeViewItem)temp.Parent);
                sep = @"\";
            }
            FileInfo fileInfo = new(path);
            string extension = fileInfo.Extension;
            if (MyRegex().IsMatch(path))
            {
                vm.SelectedImagePath = path;

                HecRasHdfReader.InspectPlanData(path);

                EventBus.PublishHdfPathA(path);

                var profiles = HecRasHdfReader.GetProfileNames(path);
                var planName = HecRasHdfReader.GetPlanName(path);

                System.Diagnostics.Debug.WriteLine($"Profiles found: {profiles.Count}, Plan: {planName}");

                EventBus.PublishHdfFileA([.. profiles], planName);
            }

            else if (extension == ".run")
            {
                string projRoot = Path.GetFullPath(Path.Combine(
                    Path.GetDirectoryName(projPath ?? string.Empty) ?? string.Empty, ".."));

                if (!path.StartsWith(projRoot, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(
                        $"Selected HMS file must be within the project root folder:\n{projRoot}",
                        "Invalid HMS Path",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                EventBus.PublishRunPath(path);

            }
            else if (extension == ".prj")
            {
                if (IsHecRasProjectFile(path)) EventBus.PublishProjPath(path);
            }
        }



        private static bool IsHecRasProjectFile(string filePath)
        {
            try
            {
                using StreamReader reader = new(filePath);
                var firstLine = reader.ReadLine()?.Trim();
                return firstLine?.StartsWith("Proj Title", StringComparison.OrdinalIgnoreCase) == true;
            }
            catch
            {
                return false;
            }
        }



        private readonly StringBuilder Contents = new();

        private void ExploreAPath(string Path)
        {
            Contents.Append("Contnet of DIR " + Path + " : \r\n");

            string[] Files = System.IO.Directory.GetFiles(Path);
            for (int i = 0; i < Files.Length; i++)
            {
                Contents.Append("\t" + Files[i] + "\r\n");
            }

            string[] Directories = System.IO.Directory.GetDirectories(Path);
            for (int i = 0; i < Directories.Length; i++)
            {
                ExploreAPath(Directories[i]);
            }

            System.Diagnostics.Debug.WriteLine($"ExploreAPath: {Files}\n");
        }

        [GeneratedRegex(@"\.p\d+\.hdf$", System.Text.RegularExpressions.RegexOptions.IgnoreCase, "en-US")]
        private static partial Regex MyRegex();



        private async Task ExpandToPath(string targetPath)
        {
            if (string.IsNullOrEmpty(targetPath) || !Directory.Exists(targetPath)) return;

            TreeViewItem? currentItem = null;
            foreach (TreeViewItem item in foldersItem.Items)
            {
                string header = item.Tag?.ToString() ?? string.Empty;
                if (targetPath.StartsWith(header, StringComparison.OrdinalIgnoreCase))
                {
                    currentItem = item;
                    break;
                }
            }

            if (currentItem == null) return;

            string rootTag = currentItem.Tag?.ToString() ?? string.Empty;
            string remainingPath = targetPath[rootTag.TrimEnd('\\').Length..].TrimStart('\\');
            var parts = remainingPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in parts)
            {
                currentItem.IsExpanded = true;
                await Task.Delay(100);
                Application.Current.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);

                TreeViewItem? nextItem = null;
                foreach (TreeViewItem child in currentItem.Items)
                {
                    if (child.Header?.ToString()?.Equals(part, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        nextItem = child;
                        break;
                    }
                }

                if (nextItem == null) break;
                currentItem = nextItem;
            }

            if (currentItem != null)
            {
                currentItem.IsSelected = true;
                currentItem.BringIntoView();
            }
        }







        private async Task InitializeAsync()
        {
            _isLoading = true;

            string rootPath = @"C:\Temp\";
            if (Directory.Exists(rootPath))
            {
                TreeViewItem rootItem = new()
                {
                    Header = rootPath,
                    Tag = rootPath,
                    FontWeight = FontWeights.Normal
                };
                rootItem.Items.Add(dummyNode);
                rootItem.Expanded += new RoutedEventHandler(Folder_Expanded);
                foldersItem.Items.Add(rootItem);
            }

            settings = await _settingsRepo.GetSettings();

            projPath = settings.LastProjPath;
            txtBoxProjPath.Text = projPath;

            projDir = Directory.Exists(projPath)
                ? projPath
                : Path.GetDirectoryName(projPath) ?? string.Empty;

            PopulateHdfComboBox(projDir);

            if (!string.IsNullOrEmpty(projPath) && settings.Projects.TryGetValue(projPath, out var projSettings))
            {
                cboxHdfPathA.SelectedItem = cboxHdfPathA.Items
                    .Cast<string>()
                    .FirstOrDefault(f => f.Equals(projSettings.HdfPathA, StringComparison.OrdinalIgnoreCase));

                var allFiles = (cboxHdfPathA.ItemsSource as List<string>) ?? [];
                UpdateHdfPathB(allFiles, projSettings.HdfPathA);

                cboxHdfPathB.SelectedItem = cboxHdfPathB.Items
                    .Cast<string>()
                    .FirstOrDefault(f => f.Equals(projSettings.HdfPathB, StringComparison.OrdinalIgnoreCase));

                txtBoxPlanNameA.Text = projSettings.PlanNameA;
                txtBoxPlanNameB.Text = projSettings.PlanNameB;
                txtBoxHmsPath.Text = projSettings.HmsPath;

                if (!string.IsNullOrEmpty(projSettings.HdfPathA) && File.Exists(projSettings.HdfPathA))
                {
                    try
                    {
                        var profiles = HecRasHdfReader.GetProfileNames(projSettings.HdfPathA);
                        cboxProfiles.ItemsSource = profiles;
                        cboxProfiles.SelectedItem = projSettings.ProName;
                        txtBoxPlanNameA.Text = projSettings.PlanNameA;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error loading HDF on startup: {ex.Message}");
                    }
                }
            }
            _isLoading = false;
            if (!string.IsNullOrEmpty(projDir))
                await ExpandToPath(projDir);
        }


        private void PopulateHdfComboBox(string projTitle)
        {
            if (string.IsNullOrEmpty(projTitle) || !Directory.Exists(projTitle))
                return;

            List<string> hdfFiles = [.. Directory.GetFiles(projTitle, "*.hdf").Where(f => MyRegex1().IsMatch(Path.GetFileName(f)))];

            cboxHdfPathA.ItemsSource = hdfFiles.ToList();

            UpdateHdfPathB(hdfFiles, cboxHdfPathA.SelectedItem as string);
        }

        private void UpdateHdfPathB(List<string> allFiles, string? excludePath)
        {
            _isLoading = true;
            var savedB = cboxHdfPathB.SelectedItem as string;

            List<string> hdfFilesB = [.. allFiles.Where(f =>
                !f.Equals(excludePath, StringComparison.OrdinalIgnoreCase))];

            cboxHdfPathB.ItemsSource = hdfFilesB;
            if (!string.IsNullOrEmpty(savedB))
                cboxHdfPathB.SelectedItem = hdfFilesB
                    .FirstOrDefault(f => f.Equals(savedB, StringComparison.OrdinalIgnoreCase));

            _isLoading = false;
        }








        [GeneratedRegex(@"\.p\d+\.hdf$", RegexOptions.IgnoreCase, "en-US")]
        private static partial Regex MyRegex1();

        private async void OnSettingsChanged(object sender, RoutedEventArgs e)
        {
            await SaveSettings();
        }


        private async Task OnProjPathChanged(string projPath)
        {
            if (string.IsNullOrEmpty(projPath)) return;

            string projDir = Directory.Exists(projPath)
                ? projPath
                : Path.GetDirectoryName(projPath) ?? string.Empty;

            if (string.IsNullOrEmpty(projDir)) return;

            this.projPath = projPath;  // keep class field in sync
            this.projDir = projDir;

            PopulateHdfComboBox(projDir);

            var settings = await _settingsRepo.GetSettings();
            settings.LastProjPath = projPath;
            settings.ProjPath = projPath;
            settings.ProjDir = projDir;

            if (settings.Projects.TryGetValue(projPath, out var existingSettings))
            {
                _isLoading = true;

                cboxHdfPathA.SelectedItem = cboxHdfPathA.Items
                    .Cast<string>()
                    .FirstOrDefault(f => f.Equals(existingSettings.HdfPathA, StringComparison.OrdinalIgnoreCase));

                var allFiles = (cboxHdfPathA.ItemsSource as List<string>) ?? [];
                UpdateHdfPathB(allFiles, existingSettings.HdfPathA);

                cboxHdfPathB.SelectedItem = cboxHdfPathB.Items
                    .Cast<string>()
                    .FirstOrDefault(f => f.Equals(existingSettings.HdfPathB, StringComparison.OrdinalIgnoreCase));

                txtBoxPlanNameA.Text = existingSettings.PlanNameA;
                txtBoxPlanNameB.Text = existingSettings.PlanNameB;

                // Validate stored HMS path still belongs to this project root
                string projRoot = Path.GetFullPath(Path.Combine(
                    Path.GetDirectoryName(projPath) ?? string.Empty, ".."));

                string storedHms = existingSettings.HmsPath ?? string.Empty;
                bool hmsValid = !string.IsNullOrEmpty(storedHms)
                    && File.Exists(storedHms)
                    && storedHms.StartsWith(projRoot, StringComparison.OrdinalIgnoreCase);

                txtBoxHmsPath.Text = hmsValid ? storedHms : string.Empty;
                if (!hmsValid) existingSettings.HmsPath = string.Empty;

                if (!string.IsNullOrEmpty(existingSettings.HdfPathA) && File.Exists(existingSettings.HdfPathA))
                {
                    try
                    {
                        var profiles = HecRasHdfReader.GetProfileNames(existingSettings.HdfPathA);
                        cboxProfiles.ItemsSource = profiles;
                        cboxProfiles.SelectedItem = existingSettings.ProName;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error loading HDF: {ex.Message}");
                    }
                }

                existingSettings.LastOpened = DateTime.Now;
                _isLoading = false;
            }
            else
            {
                txtBoxHmsPath.Text = string.Empty;

                settings.Projects[projPath] = new ProjectSettings
                {
                    ProjDir = projDir,
                    LastOpened = DateTime.Now
                };

                System.Diagnostics.Debug.WriteLine($"New project, loading defaults for {projPath}");

                if (cboxHdfPathA.Items.Count > 0)
                {
                    cboxHdfPathA.SelectedIndex = 0;
                    if (cboxHdfPathA.SelectedItem is string selectedHdfA && File.Exists(selectedHdfA))
                    {
                        try
                        {
                            var profiles = HecRasHdfReader.GetProfileNames(selectedHdfA);
                            var planName = HecRasHdfReader.GetPlanName(selectedHdfA);
                            txtBoxPlanNameA.Text = planName;
                            cboxProfiles.ItemsSource = profiles;
                            if (profiles.Count > 0)
                            {
                                cboxProfiles.SelectedIndex = 0;
                                settings.Projects[projPath].ProName = profiles[0];
                            }

                            await _settingsRepo.SaveSettings(settings);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error loading HDF A: {ex.Message}");
                        }
                    }
                }
                if (cboxHdfPathB.Items.Count > 1)
                    cboxHdfPathB.SelectedIndex = 1;
                else if (cboxHdfPathB.Items.Count > 0)
                {
                    cboxHdfPathB.SelectedIndex = 0;
                    if (cboxHdfPathB.SelectedItem is string selectedHdfB && File.Exists(selectedHdfB))
                    {
                        try
                        {
                            var planName = HecRasHdfReader.GetPlanName(selectedHdfB);
                            txtBoxPlanNameB.Text = planName;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error loading HDF B: {ex.Message}");
                        }
                    }
                }
            }

            await _settingsRepo.SaveSettings(settings);
            EventBus.PublishProjPathChanged(projPath);
        }









        private async Task SaveSettings()
        {
            _lastSaveRequest = DateTime.Now;
            var requestTime = _lastSaveRequest;

            await Task.Delay(500);

            if (_lastSaveRequest != requestTime) return;

            try
            {
                string projPath = txtBoxProjPath.Text;
                if (string.IsNullOrEmpty(projPath)) return;

                var settings = await _settingsRepo.GetSettings();

                settings.LastProjPath = projPath;
                settings.ProjPath = projPath;
                settings.ProjDir = Directory.Exists(projPath)
                    ? projPath
                    : Path.GetDirectoryName(projPath) ?? string.Empty;

                // Preserve existing LastOpened if the project already exists
                var existingLastOpened = settings.Projects.TryGetValue(projPath, out var existing)
                    ? existing.LastOpened
                    : DateTime.Now;

                settings.Projects[projPath] = new ProjectSettings
                {
                    ProjDir = settings.ProjDir,
                    HdfPathA = cboxHdfPathA.SelectedItem as string ?? string.Empty,
                    HdfPathB = cboxHdfPathB.SelectedItem as string ?? string.Empty,
                    PlanNameA = txtBoxPlanNameA.Text,
                    PlanNameB = txtBoxPlanNameB.Text,
                    ProName = cboxProfiles.SelectedItem as string ?? string.Empty,
                    HmsPath = txtBoxHmsPath.Text,
                    LastOpened = existingLastOpened
                };

                var hmsPath = txtBoxHmsPath.Text;
                if (!string.IsNullOrEmpty(hmsPath))
                    settings.HmsProjects[hmsPath] = DateTime.Now;

                await _settingsRepo.SaveSettings(settings);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SaveSettings error: {ex.Message}");
            }
        }
    }
}