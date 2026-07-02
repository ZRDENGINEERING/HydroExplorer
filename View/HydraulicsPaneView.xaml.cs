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
        private string _projPath = string.Empty;
        private ModelDimensions _modelDims = ModelDimensions.Unknown;
        private static string? _lastModelDimsPlanPath;
        private static ModelDimensions _lastModelDimsResult = ModelDimensions.Unknown;


        public HydraulicsPaneView()
        {
            InitializeComponent();
            _settingsRepo = App.ServiceProvider.GetRequiredService<IUserSettingsRepo>();

            EventBus.AppLoaded += OnAppLoaded;

            EventBus.ProjPathSelected += async path =>
            {
                _isLoading = true;

                _projPath = PathHelpers.NormalizeProjKey(path);

                _projDir = Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? string.Empty;
                PopulateHdfComboBox(_projDir);

                var settings = await _settingsRepo.GetSettingsFresh();
                string projKey = PathHelpers.NormalizeProjKey(path);

                try
                {
                    string? planNameA = null;
                    string? planNameB = null;
                    string? savedProName = null;
                    string hdfPathAToExclude = string.Empty;

                    if (!string.IsNullOrEmpty(projKey) &&
                        settings.Projects.TryGetValue(projKey, out var proj))
                    {
                        planNameA = _hdfPlanNames.FirstOrDefault(kv =>
                            kv.Key.Equals(proj.HdfPathA, StringComparison.OrdinalIgnoreCase)).Value;
                        planNameB = _hdfPlanNames.FirstOrDefault(kv =>
                            kv.Key.Equals(proj.HdfPathB, StringComparison.OrdinalIgnoreCase)).Value;
                        hdfPathAToExclude = proj.HdfPathA;
                        savedProName = proj.ProName;
                    }

                    if (planNameA == null)
                    {
                        var allPlanNames = cboxPlanNameA.ItemsSource as List<string>;
                        planNameA = allPlanNames?.FirstOrDefault();
                        if (planNameA != null)
                            hdfPathAToExclude = PlanNameToPath(planNameA) ?? string.Empty;
                    }

                    cboxPlanNameA.SelectedItem = planNameA;

                    var allFiles = (cboxPlanNameA.Tag as List<string>) ?? [];
                    UpdateHdfPathB(allFiles, hdfPathAToExclude, planNameB);

                    if (cboxPlanNameB.SelectedItem == null && cboxPlanNameB.Items.Count > 0)
                        cboxPlanNameB.SelectedIndex = 0;

                    string? planAPath = PlanNameToPath(planNameA);
                    string? planBPath = cboxPlanNameB.SelectedItem is string b ? PlanNameToPath(b) : null;

                    txtBoxHdfPathA.Text = Path.GetFileName(planAPath) ?? string.Empty;
                    txtBoxHdfPathB.Text = Path.GetFileName(planBPath) ?? string.Empty;

                    UpdateModelDimensions(planAPath);

                    if (!string.IsNullOrEmpty(planAPath) && File.Exists(planAPath))
                    {
                        var profiles = HecRasHdfReader.GetProfileNames(planAPath);
                        cboxProfiles.ItemsSource = profiles;
                        SelectDefaultProfile(profiles, savedProName);
                    }
                }
                finally
                {
                    _isLoading = false;
                }

                if (cboxProfiles.SelectedItem is string profile)
                    EventBus.PublishProfileChanged(profile);

                await SaveSettings();
            };




            EventBus.HdfFileASelected += (profiles, planName) =>
            {
                cboxProfiles.ItemsSource = profiles;
                txtBoxHdfPathA.Text = Path.GetFileName(planName);
            };

            EventBus.HdfFileBSelected += (profiles, planName) =>
            {
                cboxProfiles.ItemsSource = profiles;
                txtBoxHdfPathB.Text = Path.GetFileName(planName);
            };

            EventBus.ProfileMismatchWarning += msg =>
                Dispatcher.Invoke(() => txtProfileWarning.Text = msg);

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

                txtBoxHdfPathA.Text = Path.GetFileName(path);

                UpdateModelDimensions(path);

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

                txtBoxHdfPathB.Text = Path.GetFileName(path);
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
                _isLoading = true;
                try
                {
                    string projPath = PathHelpers.NormalizeProjKey(settings.LastProjPath);
                    _projDir = Directory.Exists(projPath)
                        ? projPath
                        : Path.GetDirectoryName(projPath) ?? string.Empty;

                    PopulateHdfComboBox(_projDir);

                    if (string.IsNullOrEmpty(projPath) ||
                        !settings.Projects.TryGetValue(projPath, out var proj)) return;

                    string? planNameA = _hdfPlanNames.FirstOrDefault(kv =>
                        kv.Key.Equals(proj.HdfPathA, StringComparison.OrdinalIgnoreCase)).Value;
                    string? planNameB = _hdfPlanNames.FirstOrDefault(kv =>
                        kv.Key.Equals(proj.HdfPathB, StringComparison.OrdinalIgnoreCase)).Value;

                    cboxPlanNameA.SelectedItem = planNameA;

                    var allFiles = (cboxPlanNameA.Tag as List<string>) ?? [];
                    UpdateHdfPathB(allFiles, proj.HdfPathA, planNameB);

                    txtBoxHdfPathA.Text = Path.GetFileName(proj.HdfPathA);
                    txtBoxHdfPathB.Text = Path.GetFileName(proj.HdfPathB);

                    UpdateModelDimensions(proj.HdfPathA);

                    if (!string.IsNullOrEmpty(proj.HdfPathA) && File.Exists(proj.HdfPathA))
                    {
                        var profiles = HecRasHdfReader.GetProfileNames(proj.HdfPathA);
                        cboxProfiles.ItemsSource = profiles;
                        SelectDefaultProfile(profiles, proj.ProName);
                    }
                    EventBus.PublishPlanNamesChanged(
                        planNameA ?? string.Empty,
                        cboxPlanNameB.SelectedItem as string ?? string.Empty);
                }
                finally { _isLoading = false; }

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

        /// <summary>
        /// Resolves the geometry HDF paired to the given plan HDF via the .prj/plan
        /// file chain (HecRasPrjReader.ResolveGeomHdf), then peeks that single
        /// geometry HDF for 1D cross sections / 2D flow areas. Updates _modelDims.
        /// Cheap — only opens the one paired geometry file, not the whole project dir.
        /// No-ops if called again for the same plan path it was just resolved for —
        /// guards against duplicate upstream triggers (e.g. ProjPathSelected firing
        /// twice for one selection, or this view being constructed more than once
        /// and double-subscribing to the static EventBus) re-opening the HDF and
        /// re-logging needlessly.
        /// </summary>
        private void UpdateModelDimensions(string? planAPath)
        {
            if (string.IsNullOrEmpty(planAPath) || !File.Exists(planAPath))
            {
                _modelDims = ModelDimensions.Unknown;
                _lastModelDimsPlanPath = planAPath;
                return;
            }

            if (string.Equals(planAPath, _lastModelDimsPlanPath, StringComparison.OrdinalIgnoreCase))
            {
                _modelDims = _lastModelDimsResult;
                //System.Diagnostics.Debug.WriteLine(
                //    $"UpdateModelDimensions: skipping duplicate call for '{Path.GetFileName(planAPath)}' (already {_modelDims}).");
                return;
            }
            _lastModelDimsPlanPath = planAPath;

            string? geomHdf = HecRasPrjReader.ResolveGeomHdf(planAPath);

            if (geomHdf == null)
            {
                _modelDims = ModelDimensions.Unknown;
                _lastModelDimsResult = _modelDims;
                System.Diagnostics.Debug.WriteLine(
                    $"UpdateModelDimensions: could not resolve geometry HDF for '{planAPath}'.");
                return;
            }

            var (has1D, has2D) = HecRasHdfReader.GetModelDimensions(geomHdf);

            _modelDims = (has1D, has2D) switch
            {
                (true, true) => ModelDimensions.Mixed,
                (false, true) => ModelDimensions.TwoDOnly,
                (true, false) => ModelDimensions.OneDOnly,
                _ => ModelDimensions.Unknown
            };
            _lastModelDimsResult = _modelDims;

            //System.Diagnostics.Debug.WriteLine(
            //    $"UpdateModelDimensions: plan='{Path.GetFileName(planAPath)}' geom='{Path.GetFileName(geomHdf)}' → {_modelDims}");
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
                string projPath = _projPath;
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


        [GeneratedRegex(@"\.p\d+\.hdf$", RegexOptions.IgnoreCase, "en-US")]
        private static partial Regex HdfRegex();
    }
}