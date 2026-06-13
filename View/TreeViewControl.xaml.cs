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

        private Dictionary<string, string> _hdfPlanNames = [];





        public TreeViewControl()
        {
            InitializeComponent();

            _settingsRepo = App.ServiceProvider.GetRequiredService<IUserSettingsRepo>();

            EventBus.HdfFileASelected += (profiles, planName) =>
            {
                cboxProfiles.ItemsSource = profiles;
                txtBoxHdfPathA.Text = planName;
            };



            EventBus.HdfFileBSelected += (profiles, planName) =>
            {
                cboxProfiles.ItemsSource = profiles;
                txtBoxHdfPathB.Text = planName;
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
                if (_isLoading) return;
                try
                {
                    if (cboxProfiles.SelectedItem is string selectedProfile)
                    {
                        cboxProfiles.Text = selectedProfile;
                        EventBus.PublishProfileChanged(selectedProfile);
                        await SaveSettings();
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"cboxProfiles error: {ex.Message}");
                }
            };


            cboxPlanNameA.SelectionChanged += async (s, e) =>
            {
                if (_isLoading) return;
                try
                {
                    if (cboxPlanNameA.SelectedItem is string selectedPlanName)
                    {
                        var selectedPath = PlanNameToPath(selectedPlanName);
                        if (selectedPath == null) return;

                        var allFiles = (cboxPlanNameA.Tag as List<string>) ?? [];
                        UpdateHdfPathB(allFiles, selectedPath);

                        var profiles = HecRasHdfReader.GetProfileNames(selectedPath);
                        cboxProfiles.ItemsSource = profiles;
                        txtBoxHdfPathA.Text = selectedPath; // TextBox shows the HDF path
                        await SaveSettings();
                        EventBus.PublishHdfPathChanged();
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"cboxPlanNameA error: {ex.Message}"); }
            };

            cboxPlanNameB.SelectionChanged += async (s, e) =>
            {
                if (_isLoading) return;
                try
                {
                    if (cboxPlanNameB.SelectedItem is string selectedPlanName)
                    {
                        var selectedPath = PlanNameToPath(selectedPlanName);
                        if (selectedPath == null) return;

                        var profiles = HecRasHdfReader.GetProfileNames(selectedPath);
                        cboxProfiles.ItemsSource = profiles;
                        txtBoxHdfPathB.Text = selectedPath; // TextBox shows the HDF path
                        await SaveSettings();
                        EventBus.PublishHdfPathChanged();
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"cboxPlanNameB error: {ex.Message}"); }
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

                EventBus.PublishHdfFileA([.. profiles], planName);

            }

            else if (extension == ".run")
            {
                string projRoot = Path.GetFullPath(Path.Combine(
                    Path.GetDirectoryName(projPath ?? string.Empty) ?? string.Empty, ".."));

                //if (!path.StartsWith(projRoot, StringComparison.OrdinalIgnoreCase))
                //{
                //    MessageBox.Show(
                //        $"Selected HMS file should be within the project root folder:\n{projRoot}",
                //        "Verify HMS Path",
                //        MessageBoxButton.OK,
                //        MessageBoxImage.Warning);
                //    return;
                //}

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

                rootItem.Loaded += (s, e) =>
                {
                    if (s is not TreeViewItem tvi) return;
                    if (tvi.Template.FindName("Bd", tvi) is Border bd)
                    {
                        bd.Background = System.Windows.Media.Brushes.Transparent;
                        tvi.MouseEnter += (_, _) => bd.Background = System.Windows.Media.Brushes.Transparent;
                        tvi.MouseLeave += (_, _) => bd.Background = System.Windows.Media.Brushes.Transparent;
                    }
                };

                rootItem.Items.Add(dummyNode);
                rootItem.Expanded += new RoutedEventHandler(Folder_Expanded);
                foldersItem.Items.Add(rootItem);
            }


            settings = await _settingsRepo.GetSettings();

            projPath = NormalizeProjKey(settings.LastProjPath);
            settings.LastProjPath = projPath;
            txtBoxProjPath.Text = projPath;

            projDir = Directory.Exists(projPath)
                ? projPath
                : Path.GetDirectoryName(projPath) ?? string.Empty;

            PopulateHdfComboBox(projDir);


            if (!string.IsNullOrEmpty(projPath) && settings.Projects.TryGetValue(projPath, out var projSettings))
            {
                _isLoading = true;

                cboxPlanNameA.SelectedItem = cboxPlanNameA.Items
                    .Cast<string>()
                    .FirstOrDefault(name =>
                    {
                        var path = PlanNameToPath(name);
                        return path?.Equals(projSettings.HdfPathA, StringComparison.OrdinalIgnoreCase) == true;
                    });
                
                var allFiles = (cboxPlanNameA.Tag as List<string>) ?? [];
                UpdateHdfPathB(allFiles, projSettings.HdfPathA);

                // Restore plan name B — match by path, display plan name
                cboxPlanNameB.SelectedItem = cboxPlanNameB.Items
                    .Cast<string>()
                    .FirstOrDefault(name =>
                    {
                        var path = PlanNameToPath(name);
                        return path?.Equals(projSettings.HdfPathB, StringComparison.OrdinalIgnoreCase) == true;
                    });

                txtBoxHdfPathA.Text = projSettings.HdfPathA;
                txtBoxHdfPathB.Text = projSettings.HdfPathB;
                txtBoxHmsPath.Text = projSettings.HmsPath;

                if (!string.IsNullOrEmpty(projSettings.HdfPathA) && File.Exists(projSettings.HdfPathA))
                {
                    try
                    {
                        var profiles = HecRasHdfReader.GetProfileNames(projSettings.HdfPathA);
                        cboxProfiles.ItemsSource = profiles;
                        SelectDefaultProfile(profiles, projSettings.ProName);
                        txtBoxHdfPathA.Text = projSettings.HdfPathA;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error loading HDF on startup: {ex.Message}");
                    }
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"InitializeAsync: No project settings found for key '{projPath}'");
            }

            _isLoading = false;

            if (!string.IsNullOrEmpty(projDir))
                await ExpandToPath(projDir);
        }


        private void SelectDefaultProfile(IList<string> profiles, string? savedProfile)
        {
            if (profiles.Count == 0) return;

            string? match = null;

            if (!string.IsNullOrEmpty(savedProfile))
                match = profiles.FirstOrDefault(p => p.Equals(savedProfile, StringComparison.OrdinalIgnoreCase));

            if (match == null)
                match = profiles.FirstOrDefault(p => p.Contains("100", StringComparison.OrdinalIgnoreCase));

            if (match == null)
                match = profiles[0];

            cboxProfiles.SelectedItem = match;
        }

        private void FoldersItem_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            SetRootItemsNoHover();
        }

        private void SetRootItemsNoHover()
        {
            foreach (var item in foldersItem.Items.OfType<TreeViewItem>())
            {
                item.Background = System.Windows.Media.Brushes.Transparent;
            }
        }




        private void PopulateHdfComboBox(string projTitle)
        {
            if (string.IsNullOrEmpty(projTitle) || !Directory.Exists(projTitle))
                return;

            List<string> hdfFiles = [.. Directory.GetFiles(projTitle, "*.hdf")
        .Where(f => MyRegex1().IsMatch(Path.GetFileName(f)))];

            // Build path → plan name lookup
            _hdfPlanNames = hdfFiles.ToDictionary(
                f => f,
                f => { try { return HecRasHdfReader.GetPlanName(f); } catch { return Path.GetFileName(f); } }
            );

            // ComboBox displays plan names, Tag/SelectedValue resolves back to path
            cboxPlanNameA.ItemsSource = hdfFiles.Select(f => _hdfPlanNames[f]).ToList();
            cboxPlanNameA.Tag = hdfFiles; // keep full paths accessible

            UpdateHdfPathB(hdfFiles, null);
        }

        private string? PlanNameToPath(string? planName)
        {
            if (string.IsNullOrEmpty(planName)) return null;
            return _hdfPlanNames.FirstOrDefault(kv => kv.Value == planName).Key;
        }



        private void UpdateHdfPathB(List<string> allFiles, string? excludePath)
        {
            _isLoading = true;
            var savedB = cboxPlanNameB.SelectedItem as string;

            List<string> hdfFilesB = [.. allFiles.Where(f =>
        !f.Equals(excludePath, StringComparison.OrdinalIgnoreCase))];

            cboxPlanNameB.ItemsSource = hdfFilesB.Select(f =>
                _hdfPlanNames.TryGetValue(f, out var name) ? name : Path.GetFileName(f)).ToList();
            cboxPlanNameB.Tag = hdfFilesB;

            if (!string.IsNullOrEmpty(savedB))
                cboxPlanNameB.SelectedItem = savedB; // already a plan name string

            _isLoading = false;
        }








        [GeneratedRegex(@"\.p\d+\.hdf$", RegexOptions.IgnoreCase, "en-US")]
        private static partial Regex MyRegex1();

        private async void OnSettingsChanged(object sender, RoutedEventArgs e)
        {
            await SaveSettings();
        }


        private async Task OnProjPathChanged(string rawPath)
        {
            string projPath = NormalizeProjKey(rawPath);

            if (string.IsNullOrEmpty(projPath)) return;

            string projDir = Directory.Exists(projPath)
                ? projPath
                : Path.GetDirectoryName(projPath) ?? string.Empty;

            if (string.IsNullOrEmpty(projDir)) return;

            this.projPath = NormalizeProjKey(projPath);
            this.projDir = projDir;

            PopulateHdfComboBox(projDir);

            var settings = await _settingsRepo.GetSettings();
            settings.LastProjPath = projPath;
            settings.ProjPath = projPath;
            settings.ProjDir = projDir;

            if (settings.Projects.TryGetValue(projPath, out var existingSettings))
            {
                _isLoading = true;

                cboxPlanNameA.SelectedItem = cboxPlanNameA.Items
                    .Cast<string>()
                    .FirstOrDefault(name =>
                    {
                        var path = PlanNameToPath(name);
                        return path?.Equals(existingSettings.HdfPathA, StringComparison.OrdinalIgnoreCase) == true;
                    });

                var allFiles = (cboxPlanNameA.Tag as List<string>) ?? [];
                UpdateHdfPathB(allFiles, existingSettings.HdfPathA);

                cboxPlanNameB.SelectedItem = cboxPlanNameB.Items
                    .Cast<string>()
                    .FirstOrDefault(name =>
                    {
                        var path = PlanNameToPath(name);
                        return path?.Equals(existingSettings.HdfPathB, StringComparison.OrdinalIgnoreCase) == true;
                    });

                txtBoxHdfPathA.Text = existingSettings.HdfPathA;
                txtBoxHdfPathB.Text = existingSettings.HdfPathB;


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
                        SelectDefaultProfile(profiles, existingSettings.ProName);
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

                if (cboxPlanNameA.Items.Count > 0)
                {
                    cboxPlanNameA.SelectedIndex = 0;
                    if (cboxPlanNameA.SelectedItem is string selectedPlanName)
                    {
                        var selectedPath = PlanNameToPath(selectedPlanName);
                        if (selectedPath != null && File.Exists(selectedPath))
                        {
                            try
                            {
                                txtBoxHdfPathA.Text = selectedPath;

                                var profiles = HecRasHdfReader.GetProfileNames(selectedPath);
                                cboxProfiles.ItemsSource = profiles;
                                SelectDefaultProfile(profiles, null);
                                if (cboxProfiles.SelectedItem is string selected)
                                    settings.Projects[projPath].ProName = selected;

                                await _settingsRepo.SaveSettings(settings);
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error loading HDF A: {ex.Message}");
                            }
                        }
                    }
                }

                if (cboxPlanNameB.Items.Count > 1)
                    cboxPlanNameB.SelectedIndex = 1;
                else if (cboxPlanNameB.Items.Count > 0)
                {
                    cboxPlanNameB.SelectedIndex = 0;
                    if (cboxPlanNameB.SelectedItem is string selectedPlanNameB)
                    {
                        var selectedPathB = PlanNameToPath(selectedPlanNameB);
                        if (selectedPathB != null && File.Exists(selectedPathB))
                        {
                            try
                            {
                                txtBoxHdfPathB.Text = selectedPathB;
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
            await ExpandToPath(projDir);
        }






        private async Task SaveSettings()
        {
            if (_isLoading) return;

            _lastSaveRequest = DateTime.Now;
            var requestTime = _lastSaveRequest;

            await Task.Delay(500);
            if (_lastSaveRequest != requestTime) return;

            try
            {
                string projPath = NormalizeProjKey(txtBoxProjPath.Text);
                if (string.IsNullOrEmpty(projPath)) return;

                var settings = await _settingsRepo.GetSettings();

                settings.LastProjPath = projPath;
                settings.ProjPath = projPath;
                settings.ProjDir = Directory.Exists(projPath)
                    ? projPath
                    : Path.GetDirectoryName(projPath) ?? string.Empty;

                var existingLastOpened = settings.Projects.TryGetValue(projPath, out var existing)
                    ? existing.LastOpened
                    : DateTime.Now;

                int openOrder = settings.NextOpenOrder++;

                string? selectedPlanNameA = cboxPlanNameA.SelectedItem as string;
                string? selectedPlanNameB = cboxPlanNameB.SelectedItem as string;
                string? resolvedPathA = PlanNameToPath(selectedPlanNameA);
                string? resolvedPathB = PlanNameToPath(selectedPlanNameB);

                settings.Projects[projPath] = new ProjectSettings
                {
                    ProjDir = settings.ProjDir,
                    HdfPathA = resolvedPathA ?? string.Empty,
                    HdfPathB = resolvedPathB ?? string.Empty,
                    PlanNameA = selectedPlanNameA ?? string.Empty,
                    PlanNameB = selectedPlanNameB ?? string.Empty,
                    ProName = cboxProfiles.SelectedItem as string ?? string.Empty,
                    HmsPath = txtBoxHmsPath.Text,
                    LastOpened = existingLastOpened,
                    OpenOrder = openOrder

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

        private void TreeViewItem_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
        {
            if (sender is TreeViewItem tvi)
            {
                e.Handled = true;
            }
        }




        internal static string NormalizeProjKey(string projPath)
        {
            if (string.IsNullOrEmpty(projPath)) return projPath;

            // If it's a .prj, check if a .rasmap sibling exists — prefer that as the key
            if (projPath.EndsWith(".prj", StringComparison.OrdinalIgnoreCase))
            {
                string dir = Path.GetDirectoryName(projPath) ?? string.Empty;
                string stem = Path.GetFileNameWithoutExtension(projPath);
                string rasmap = Path.Combine(dir, stem + ".rasmap");
                if (File.Exists(rasmap)) return rasmap;
            }

            return projPath;
        }


    }


}