using BruTile.Wmts.Generated;
using HydroExplorer.Helpers;
using HydroExplorer.Utils;
using HydroExplorer.View.TabItem;
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
        private object? dummyNode = null;
        private TabInfoView tif = new();

        public event PropertyChangedEventHandler? PropertyChanged;
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

        private readonly IUserSettingsRepo _settingsRepo;
        private bool _isLoading = false;
        string? projDir = string.Empty;
        string? projPath = string.Empty;

        UserSettings settings = new();


        public TreeViewControl()
        {
            InitializeComponent();

            _settingsRepo = App.ServiceProvider.GetRequiredService<IUserSettingsRepo>();

            


            EventBus.HdfFileASelected += (profiles, planName) =>
            {
                cboxProfiles.ItemsSource = profiles;
                //cboxProfiles.SelectedIndex = 0;
                txtBoxPlanNameA.Text = planName;
            };

            EventBus.HdfFileBSelected += (profiles, planName) =>
            {
                cboxProfiles.ItemsSource = profiles;
                //cboxProfiles.SelectedIndex = 0;
                txtBoxPlanNameB.Text = planName;
            };

            EventBus.ProjPathSelected += async path =>
            {
                txtBoxProjPath.Text = path;
                await OnProjPathChanged(path);
            };

            EventBus.RunPathSelected += async path =>
            {
                txtBoxHmsPath.Text = path;
                await SaveSettings();
            };

            cboxProfiles.SelectionChanged += async (s, e) =>
            {
                if (cboxProfiles.SelectedItem is string selectedProfile)
                {
                    cboxProfiles.Text = selectedProfile;
                    await SaveSettings();
                }
            };

            cboxHdfPathA.SelectionChanged += async (s, e) =>
            {
                if (_isLoading) return;

                if (cboxHdfPathA.SelectedItem is string selectedHdf)
                {
                    try
                    {
                        var allFiles = cboxHdfPathA.ItemsSource as List<string> ?? new List<string>();
                        UpdateHdfPathB(allFiles, selectedHdf);

                        var reader = new HecRasHdfReader();
                        var profiles = reader.GetProfileNames(selectedHdf);
                        var planName = reader.GetPlanName(selectedHdf);

                        cboxProfiles.ItemsSource = profiles;
                        txtBoxPlanNameA.Text = planName;

                        await SaveSettings();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error loading HDF A: {ex.Message}");
                    }
                }
            };

            cboxHdfPathB.SelectionChanged += async (s, e) =>
            {
                if (_isLoading) return;

                if (cboxHdfPathB.SelectedItem is string selectedHdf)
                {
                    try
                    {
                        var reader = new HecRasHdfReader();
                        var profiles = reader.GetProfileNames(selectedHdf);
                        var planName = reader.GetPlanName(selectedHdf);

                        cboxProfiles.ItemsSource = profiles;
                        //cboxProfiles.SelectedIndex = 0;
                        txtBoxPlanNameB.Text = planName;

                        await SaveSettings();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error loading HDF B: {ex.Message}");
                    }
                }
            };

        }


        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            await InitializeAsync();
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
                var fileName = GetFileFolderName(filePath);

                //bool isHdf = Regex.IsMatch(fileName, @"\.p\d+\.hdf$", RegexOptions.IgnoreCase);

                //Proj Title = 6347_Channel_View_RAS

                bool isRun = fileName.EndsWith(".run", StringComparison.OrdinalIgnoreCase);
                bool isPrj = fileName.EndsWith(".prj", StringComparison.OrdinalIgnoreCase)
                                 && IsHecRasProjectFile(filePath);


                //if (!isHdf && !isPrj) return;
                if (!isPrj && !isRun) return;

                var subItem = new TreeViewItem()
                {
                    Header = fileName,
                    Tag = filePath
                };
                item.Items.Add(subItem);

                //var subItem = new TreeViewItem()
                //{
                //    Header = GetFileFolderName(filePath),
                //    Tag = filePath
                //};
                //item.Items.Add(subItem);
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
                return path.Substring(startIndex: lastIndex + 1);
            }
            //else { return path; }
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
            string extension = "";

            while (true)
            {
                string header = temp.Header.ToString();
                if (header.Contains(@"\")) sep = "";
                path = header + sep + path;

                if (temp.Parent.GetType().Equals(typeof(TreeView))) break;

                temp = ((TreeViewItem)temp.Parent);
                sep = @"\";
            }
                FileInfo fileInfo = new FileInfo(path);
                extension = fileInfo.Extension;

            if (MyRegex().IsMatch(path))
            {
                vm.SelectedImagePath = path;

                HecRasHdfReader hdfrdr = new();
                hdfrdr.InspectPlanData(path);

                EventBus.PublishHdfPathA(path);

                var reader = new HecRasHdfReader();
                var profiles = reader.GetProfileNames(path);
                var planName = reader.GetPlanName(path);

                System.Diagnostics.Debug.WriteLine($"Profiles found: {profiles.Length}, Plan: {planName}");

                EventBus.PublishHdfFileA(profiles, planName);
            }

            else if (extension == ".run")
            {
                EventBus.PublishRunPath(path);
            }
            else if (extension == ".prj")
            {
                //Proj Title = 6347_Channel_View_RAS
                if (IsHecRasProjectFile(path)) EventBus.PublishProjPath(path);
            }
        }



        private static bool IsHecRasProjectFile(string filePath)
        {
            try
            {
                using var reader = new StreamReader(filePath);
                var firstLine = reader.ReadLine();
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

            // ✅ Find which root item contains the target path
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

            // ✅ Get remaining path after root
            string rootTag = currentItem.Tag?.ToString() ?? string.Empty;
            string remainingPath = targetPath.Substring(rootTag.TrimEnd('\\').Length).TrimStart('\\');
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

            // ✅ Setup tree root first
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

                var allFiles = (cboxHdfPathA.ItemsSource as List<string>) ?? new List<string>();
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
                        var reader = new HecRasHdfReader();
                        var profiles = reader.GetProfileNames(projSettings.HdfPathA);
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

            // ✅ Expand after loading is complete
            if (!string.IsNullOrEmpty(projDir))
                await ExpandToPath(projDir);







        }









        private void PopulateHdfComboBox(string projTitle)
        {
            if (string.IsNullOrEmpty(projTitle) || !Directory.Exists(projTitle))
                return;

            var hdfFiles = Directory.GetFiles(projTitle, "*.hdf")
                .Where(f => MyRegex1().IsMatch(Path.GetFileName(f)))
                .ToList();

            cboxHdfPathA.ItemsSource = hdfFiles.ToList();

            UpdateHdfPathB(hdfFiles, cboxHdfPathA.SelectedItem as string);

            //System.Diagnostics.Debug.WriteLine($"\nFound {hdfFiles.Count} HDF files in {projPath}\n");
        }

        private void UpdateHdfPathB(List<string> allFiles, string? excludePath)
        {
            var savedB = cboxHdfPathB.SelectedItem as string;

            var hdfFilesB = allFiles
                .Where(f => !f.Equals(excludePath, StringComparison.OrdinalIgnoreCase))
                .ToList();

            cboxHdfPathB.ItemsSource = hdfFilesB;

            // ✅ Restore previous B selection if still valid
            if (!string.IsNullOrEmpty(savedB))
                cboxHdfPathB.SelectedItem = hdfFilesB
                    .FirstOrDefault(f => f.Equals(savedB, StringComparison.OrdinalIgnoreCase));
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

            // ✅ Populate HDF list from new project directory
            PopulateHdfComboBox(projDir);

            var settings = await _settingsRepo.GetSettings();
            settings.LastProjPath = projPath;

            // ✅ Check if we already have saved settings for this project
            if (settings.Projects.TryGetValue(projPath, out var existingSettings))
            {
                //System.Diagnostics.Debug.WriteLine($"Restoring existing project settings for {projPath}");

                cboxHdfPathA.SelectedItem = cboxHdfPathA.Items
                    .Cast<string>()
                    .FirstOrDefault(f => f.Equals(existingSettings.HdfPathA, StringComparison.OrdinalIgnoreCase));

                cboxHdfPathB.SelectedItem = cboxHdfPathB.Items
                    .Cast<string>()
                    .FirstOrDefault(f => f.Equals(existingSettings.HdfPathB, StringComparison.OrdinalIgnoreCase));

                txtBoxPlanNameA.Text = existingSettings.PlanNameA;
                txtBoxPlanNameB.Text = existingSettings.PlanNameB;

                if (!string.IsNullOrEmpty(existingSettings.HdfPathA) && File.Exists(existingSettings.HdfPathA))
                {
                    try
                    {
                        var reader = new HecRasHdfReader();
                        var profiles = reader.GetProfileNames(existingSettings.HdfPathA);
                        cboxProfiles.ItemsSource = profiles;
                        cboxProfiles.SelectedItem = existingSettings.ProName;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error loading HDF: {ex.Message}");
                    }
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"New project, loading defaults for {projPath}");

                // ✅ New project — auto select first HDF found
                if (cboxHdfPathA.Items.Count > 0)
                {
                    cboxHdfPathA.SelectedIndex = 0;

                    if (cboxHdfPathA.SelectedItem is string selectedHdfA && File.Exists(selectedHdfA))
                    {
                        try
                        {
                            var reader = new HecRasHdfReader();
                            var profiles = reader.GetProfileNames(selectedHdfA);
                            var planName = reader.GetPlanName(selectedHdfA);

                            txtBoxPlanNameA.Text = planName;
                            cboxProfiles.ItemsSource = profiles;
                            cboxProfiles.SelectedIndex = 0;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error loading HDF A: {ex.Message}");
                        }
                    }
                }

                if (cboxHdfPathB.Items.Count > 1)
                    cboxHdfPathB.SelectedIndex = 1; // ✅ default to second plan for comparison

                else if (cboxHdfPathB.Items.Count > 0)
                {
                    cboxHdfPathB.SelectedIndex = 0;

                    if (cboxHdfPathB.SelectedItem is string selectedHdfB && File.Exists(selectedHdfB))
                    {
                        try
                        {
                            var reader = new HecRasHdfReader();
                            var planName = reader.GetPlanName(selectedHdfB);
                            txtBoxPlanNameB.Text = planName;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error loading HDF B: {ex.Message}");
                        }
                    }
                }
            }

            EventBus.PublishProjPathChanged(projPath);

            await SaveSettings();
        }


        private async Task SaveSettings()
        {
            string projPath = txtBoxProjPath.Text;
            if (string.IsNullOrEmpty(projPath)) return;

            var settings = await _settingsRepo.GetSettings();

            settings.LastProjPath = projPath;
            settings.ProjPath = projPath;
            settings.ProjDir = Directory.Exists(projPath)
                ? projPath
                : Path.GetDirectoryName(projPath) ?? string.Empty;

            settings.Projects[projPath] = new ProjectSettings
            {
                ProjDir = settings.ProjDir,
                HdfPathA = cboxHdfPathA.SelectedItem as string ?? string.Empty,
                HdfPathB = cboxHdfPathB.SelectedItem as string ?? string.Empty,
                PlanNameA = txtBoxPlanNameA.Text,
                PlanNameB = txtBoxPlanNameB.Text,
                ProName = cboxProfiles.SelectedItem as string ?? string.Empty,
                HmsPath = txtBoxHmsPath.Text
            };

            //System.Diagnostics.Debug.WriteLine($"Saving project: {projPath}");
            //System.Diagnostics.Debug.WriteLine($"HdfPathA: {settings.Projects[projPath].HdfPathA}");

            await _settingsRepo.SaveSettings(settings); // ✅ uses FileSystemUserSettingsRepo directly
        }

















    }
}