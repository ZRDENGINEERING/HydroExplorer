using HydroExplorer.Helpers;
using HydroExplorer.Utils;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace HydroExplorer.View
{
    public partial class HydraulicsPaneView : UserControl
    {
        private readonly IUserSettingsRepo _settingsRepo;
        private bool _isLoading = false;
        private Dictionary<string, string> _hdfPlanNames = [];
        private string _projDir = string.Empty;

        public HydraulicsPaneView()
        {
            InitializeComponent();
            _settingsRepo = App.ServiceProvider.GetRequiredService<IUserSettingsRepo>();

            EventBus.AppLoaded += OnAppLoaded;

            EventBus.ProjPathSelected += async path =>
            {
                _isLoading = true;  // suppress SelectionChanged during combo rebuild + restore

                txtBoxProjPath.Text = path;
                _projDir = Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? string.Empty;
                PopulateHdfComboBox(_projDir);

                var settings = await _settingsRepo.GetSettingsFresh();
                string projKey = TreeViewControl.NormalizeProjKey(path);

                try
                {
                    if (!string.IsNullOrEmpty(projKey) &&
                        settings.Projects.TryGetValue(projKey, out var proj))
                    {
                        string? planNameA = _hdfPlanNames.FirstOrDefault(kv =>
                            kv.Key.Equals(proj.HdfPathA, StringComparison.OrdinalIgnoreCase)).Value;
                        string? planNameB = _hdfPlanNames.FirstOrDefault(kv =>
                            kv.Key.Equals(proj.HdfPathB, StringComparison.OrdinalIgnoreCase)).Value;

                        cboxPlanNameA.SelectedItem = planNameA;

                        var allFiles = (cboxPlanNameA.Tag as List<string>) ?? [];
                        UpdateHdfPathB(allFiles, proj.HdfPathA, planNameB);

                        txtBoxHdfPathA.Text = proj.HdfPathA;
                        txtBoxHdfPathB.Text = proj.HdfPathB;

                        if (!string.IsNullOrEmpty(proj.HdfPathA) && File.Exists(proj.HdfPathA))
                        {
                            var profiles = HecRasHdfReader.GetProfileNames(proj.HdfPathA);
                            cboxProfiles.ItemsSource = profiles;
                            SelectDefaultProfile(profiles, proj.ProName);
                        }
                    }
                    // No saved entry yet — combo boxes are already populated empty/default
                    // from PopulateHdfComboBox above; nothing further to restore.
                }
                finally
                {
                    _isLoading = false;
                }

                if (cboxProfiles.SelectedItem is string profile)
                    EventBus.PublishProfileChanged(profile);
            };


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

            cboxPlanNameA.SelectionChanged += async (s, e) =>
            {
                if (_isLoading) return;
                if (cboxPlanNameA.SelectedItem is not string planName) return;
                var path = PlanNameToPath(planName);
                if (path == null) return;
                var allFiles = (cboxPlanNameA.Tag as List<string>) ?? [];
                UpdateHdfPathB(allFiles, path);
                var profiles = HecRasHdfReader.GetProfileNames(path);
                cboxProfiles.ItemsSource = profiles;
                SelectDefaultProfile(profiles, null);
                txtBoxHdfPathA.Text = path;
                EventBus.PublishHdfPathChanged();
                EventBus.PublishPlanNamesChanged(
                    cboxPlanNameA.SelectedItem as string ?? string.Empty,
                    planName);
                await SaveSettings();
            };

            cboxPlanNameB.SelectionChanged += async (s, e) =>
            {
                if (_isLoading) return;
                if (cboxPlanNameB.SelectedItem is not string planName) return;
                var path = PlanNameToPath(planName);
                if (path == null) return;
                var profiles = HecRasHdfReader.GetProfileNames(path);
                cboxProfiles.ItemsSource = profiles;
                SelectDefaultProfile(profiles, null);
                txtBoxHdfPathB.Text = path;
                EventBus.PublishHdfPathChanged();
                EventBus.PublishPlanNamesChanged(
                    cboxPlanNameA.SelectedItem as string ?? string.Empty,
                    planName);
                await SaveSettings();
            };

            cboxProfiles.SelectionChanged += async (s, e) =>
            {
                if (_isLoading) return;
                if (cboxProfiles.SelectedItem is string profile)
                {
                    EventBus.PublishProfileChanged(profile);
                    await SaveSettings();
                }
            };
        }

        private void OnAppLoaded(UserSettings settings)
        {
            Dispatcher.Invoke(() =>
            {
                _isLoading = true;  // suppress all SelectionChanged during restore
                try
                {
                    string projPath = TreeViewControl.NormalizeProjKey(settings.LastProjPath);
                    _projDir = Directory.Exists(projPath)
                        ? projPath
                        : Path.GetDirectoryName(projPath) ?? string.Empty;

                    txtBoxProjPath.Text = projPath;
                    PopulateHdfComboBox(_projDir);  // populates _hdfPlanNames, sets Tag

                    if (string.IsNullOrEmpty(projPath) ||
                        !settings.Projects.TryGetValue(projPath, out var proj)) return;

                    // Resolve plan names from saved HDF paths using populated _hdfPlanNames
                    string? planNameA = _hdfPlanNames.FirstOrDefault(kv =>
                        kv.Key.Equals(proj.HdfPathA, StringComparison.OrdinalIgnoreCase)).Value;
                    string? planNameB = _hdfPlanNames.FirstOrDefault(kv =>
                        kv.Key.Equals(proj.HdfPathB, StringComparison.OrdinalIgnoreCase)).Value;

                    // Set A — _isLoading=true so SelectionChanged is suppressed
                    cboxPlanNameA.SelectedItem = planNameA;

                    // Build B list excluding A, pass desired B explicitly
                    var allFiles = (cboxPlanNameA.Tag as List<string>) ?? [];
                    UpdateHdfPathB(allFiles, proj.HdfPathA, planNameB);

                    txtBoxHdfPathA.Text = proj.HdfPathA;
                    txtBoxHdfPathB.Text = proj.HdfPathB;

                    if (!string.IsNullOrEmpty(proj.HdfPathA) && File.Exists(proj.HdfPathA))
                    {
                        var profiles = HecRasHdfReader.GetProfileNames(proj.HdfPathA);
                        cboxProfiles.ItemsSource = profiles;
                        SelectDefaultProfile(profiles, proj.ProName);
                    }
                    // Publish plan names so LP3 plot can update labels
                    EventBus.PublishPlanNamesChanged(
                        planNameA ?? string.Empty,
                        cboxPlanNameB.SelectedItem as string ?? string.Empty);
                }
                finally { _isLoading = false; }

                // Publish after _isLoading cleared so downstream consumers receive it
                if (cboxProfiles.SelectedItem is string profile)
                    EventBus.PublishProfileChanged(profile);
            });
        }

        private void PopulateHdfComboBox(string projDir)
        {
            if (string.IsNullOrEmpty(projDir) || !Directory.Exists(projDir)) return;


            var hdfFiles = Directory.GetFiles(projDir, "*.hdf")
                .Where(f => HdfRegex().IsMatch(Path.GetFileName(f)))
                .ToList();

            _hdfPlanNames = hdfFiles.ToDictionary(
                f => f,
                f => { try { return HecRasHdfReader.GetPlanName(f); } catch { return Path.GetFileName(f); } });

            cboxPlanNameA.ItemsSource = hdfFiles.Select(f => _hdfPlanNames[f]).ToList();
            cboxPlanNameA.Tag = hdfFiles;
            UpdateHdfPathB(hdfFiles, null);
        }

        private void UpdateHdfPathB(List<string> allFiles, string? excludePath, string? restoreSelection = null)
        {
            _isLoading = true;
            var savedB = restoreSelection ?? cboxPlanNameB.SelectedItem as string;
            var filesB = allFiles.Where(f => !f.Equals(excludePath, StringComparison.OrdinalIgnoreCase)).ToList();
            cboxPlanNameB.ItemsSource = filesB
                .Select(f => _hdfPlanNames.TryGetValue(f, out var n) ? n : Path.GetFileName(f))
                .ToList();
            cboxPlanNameB.Tag = filesB;
            if (!string.IsNullOrEmpty(savedB)) cboxPlanNameB.SelectedItem = savedB;
            _isLoading = false;
        }

        private string? PlanNameToPath(string? name) =>
            string.IsNullOrEmpty(name) ? null :
            _hdfPlanNames.FirstOrDefault(kv => kv.Value == name).Key;

        private void SelectDefaultProfile(IList<string> profiles, string? saved)
        {
            if (profiles.Count == 0) return;
            cboxProfiles.SelectedItem =
                profiles.FirstOrDefault(p => p.Equals(saved, StringComparison.OrdinalIgnoreCase)) ??
                profiles.FirstOrDefault(p => p.Contains("100", StringComparison.OrdinalIgnoreCase)) ??
                profiles[0];
        }

        private DateTime _lastSaveRequest = DateTime.MinValue;

        private async Task SaveSettings()
        {
            if (_isLoading) return;

            var requestTime = _lastSaveRequest = DateTime.UtcNow;
            await Task.Delay(300);
            if (_lastSaveRequest != requestTime) return;

            try
            {
                string projPath = TreeViewControl.NormalizeProjKey(txtBoxProjPath.Text);
                string planNameA = cboxPlanNameA.SelectedItem as string ?? string.Empty;
                string planNameB = cboxPlanNameB.SelectedItem as string ?? string.Empty;
                string proName = cboxProfiles.SelectedItem as string ?? string.Empty;
                string hdfPathA = PlanNameToPath(planNameA) ?? string.Empty;
                string hdfPathB = PlanNameToPath(planNameB) ?? string.Empty;

                if (string.IsNullOrEmpty(projPath)) return;

                var settings = await _settingsRepo.GetSettings();

                if (!settings.Projects.TryGetValue(projPath, out var proj))
                    proj = settings.Projects[projPath] = new ProjectSettings { ProjPath = projPath };

                // Only update HDF-related fields — preserve ProjName, HmsPath, etc.
                proj.HdfPathA = hdfPathA;
                proj.HdfPathB = hdfPathB;
                proj.PlanNameA = planNameA;
                proj.PlanNameB = planNameB;
                proj.ProName = proName;
                // ← do NOT touch proj.ProjName, proj.HmsPath, proj.ProjPath, proj.OpenOrder

                await _settingsRepo.SaveSettings(settings);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HydraulicsPaneView.SaveSettings error: {ex.Message}");
            }
        }

        private async void BrowseProjPath_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select HEC-RAS Project File",
                Filter = "HEC-RAS Project (*.prj)|*.prj|All Files (*.*)|*.*"
            };
            if (dialog.ShowDialog() != true) return;
            string path = dialog.FileName;
            txtBoxProjPath.Text = path;
            _projDir = Path.GetDirectoryName(path) ?? string.Empty;
            PopulateHdfComboBox(_projDir);
            EventBus.PublishProjPath(path);
            await SaveSettings();
        }

        private void TreeViewItem_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
            => e.Handled = true;

        [GeneratedRegex(@"\.p\d+\.hdf$", RegexOptions.IgnoreCase, "en-US")]
        private static partial Regex HdfRegex();
    }
}