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

        private string _profileWarningMessage = string.Empty;


        public HydraulicsPaneView()
        {
            InitializeComponent();
            _settingsRepo = App.ServiceProvider.GetRequiredService<IUserSettingsRepo>();

            EventBus.AppLoaded += OnAppLoaded;

            EventBus.ProjPathSelected += async path => await LoadForProject(path);
            EventBus.ProjPathChanged += async path => await LoadForProject(path);

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
            {
                _profileWarningMessage = msg;
                Dispatcher.Invoke(UpdateProfileWarningVisibility);
            };

            EventBus.TopTabChanged += topTab =>
            {
                Dispatcher.Invoke(UpdateProfileWarningVisibility);
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

                txtBoxHdfPathA.Text = Path.GetFileName(path);

                UpdateModelDimensions(path);

                // UpdateHdfPathB above may have programmatically changed Plan B's selection
                // while _isLoading suppressed its own SelectionChanged handler. Re-apply
                // explicitly so the grid/profile state matches what's actually selected.
                // ApplyPlanBSelection saves settings and publishes HdfPathChanged/PlanNamesChanged
                // itself (in that order), so no separate publish/save is needed here.
                await ApplyPlanBSelection(cboxPlanNameB.SelectedItem as string);
            };

            cboxPlanNameB.SelectionChanged += async (s, e) =>
            {
                if (_isLoading) return;
                if (cboxPlanNameB.SelectedItem is not string planName) return;
                await ApplyPlanBSelection(planName);
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

        /// <summary>
        /// Applies the effective Plan B selection (including "None") to profiles,
        /// the HDF path textbox, and downstream events. Called both from
        /// cboxPlanNameB.SelectionChanged and explicitly after any programmatic
        /// change to cboxPlanNameB.SelectedItem, since WPF won't raise
        /// SelectionChanged when the value doesn't change, and _isLoading
        /// suppresses the handler when it does.
        /// </summary>
        private async Task ApplyPlanBSelection(string? planName)
        {
            var path = PlanNameToPath(planName); // null when planName is null/"None"

            var profiles = path != null
                ? HecRasHdfReader.GetProfileNames(path)
                : (cboxPlanNameA.SelectedItem is string planA ? HecRasHdfReader.GetProfileNames(PlanNameToPath(planA)!) : new List<string>());

            cboxProfiles.ItemsSource = profiles;
            SelectDefaultProfile(profiles, null);

            txtBoxHdfPathB.Text = path != null ? Path.GetFileName(path) : string.Empty;

            // HdfPathChanged now carries the resolved paths directly, so
            // subscribers don't depend on the settings file having been
            // written yet — publish immediately, save can happen after.
            string hdfPathA = PlanNameToPath(cboxPlanNameA.SelectedItem as string) ?? string.Empty;
            EventBus.PublishHdfPathChanged(hdfPathA, path ?? string.Empty);
            EventBus.PublishPlanNamesChanged(
                cboxPlanNameA.SelectedItem as string ?? string.Empty,
                planName ?? string.Empty);
            await SaveSettings();
        }


        private async Task LoadForProject(string path)
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
                else
                {
                    cboxProfiles.ItemsSource = null;
                    txtProfileWarning.Text = string.Empty;
                    _modelDims = ModelDimensions.Unknown;
                    _lastModelDimsPlanPath = null;
                }
            }
            finally
            {
                _isLoading = false;
            }

            EventBus.PublishHdfPathChanged(
                PlanNameToPath(cboxPlanNameA.SelectedItem as string) ?? string.Empty,
                PlanNameToPath(cboxPlanNameB.SelectedItem as string) ?? string.Empty);
            EventBus.PublishPlanNamesChanged(
                cboxPlanNameA.SelectedItem as string ?? string.Empty,
                cboxPlanNameB.SelectedItem as string ?? string.Empty);

            if (cboxProfiles.SelectedItem is string selectedProfile)
                EventBus.PublishProfileChanged(selectedProfile);

            await SaveSettings();
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
                    else
                    {
                        cboxProfiles.ItemsSource = null;
                        _modelDims = ModelDimensions.Unknown;
                        _lastModelDimsPlanPath = null;

                    }

                    EventBus.PublishHdfPathChanged(
                        PlanNameToPath(planNameA) ?? string.Empty,
                        PlanNameToPath(cboxPlanNameB.SelectedItem as string) ?? string.Empty);
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

            var itemsB = new List<string> { "None" };
            itemsB.AddRange(filesB.Select(f => _hdfPlanNames.TryGetValue(f, out var n) ? n : Path.GetFileName(f)));

            cboxPlanNameB.ItemsSource = itemsB;
            cboxPlanNameB.Tag = filesB;

            if (!string.IsNullOrEmpty(savedB)) cboxPlanNameB.SelectedItem = savedB;
            else cboxPlanNameB.SelectedItem = "None";

            _isLoading = false;
        }

        private string? PlanNameToPath(string? name) =>
            string.IsNullOrEmpty(name) || name == "None" ? null :
            _hdfPlanNames.FirstOrDefault(kv => kv.Value == name).Key;

        private void SelectDefaultProfile(IList<string> profiles, string? saved)
        {
            if (profiles.Count == 0) return;
            cboxProfiles.SelectedItem =
                profiles.FirstOrDefault(p => p.Equals(saved, StringComparison.OrdinalIgnoreCase)) ??
                profiles.FirstOrDefault(p => p.Contains("100", StringComparison.OrdinalIgnoreCase)) ??
                profiles[0];
        }



        private void UpdateModelDimensions(string? planAPath)
        {
            // Every exit path funnels through here so subscribers (RAS Tables
            // guard, 2D map layer) always hear about the final value, not just
            // the "successfully resolved" case.
            void Finish()
            {
                EventBus.PublishModelDimensionsChanged(_modelDims);
                UpdateProfileWarningVisibility();
            }

            if (string.IsNullOrEmpty(planAPath) || !File.Exists(planAPath))
            {
                _modelDims = ModelDimensions.Unknown;
                _lastModelDimsPlanPath = planAPath;
                Finish();
                return;
            }

            if (string.Equals(planAPath, _lastModelDimsPlanPath, StringComparison.OrdinalIgnoreCase))
            {
                _modelDims = _lastModelDimsResult;
                //System.Diagnostics.Debug.WriteLine(
                //    $"UpdateModelDimensions: skipping duplicate call for '{Path.GetFileName(planAPath)}' (already {_modelDims}).");
                Finish();
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
                Finish();
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

            Finish();
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

                proj.HdfPathA = hdfPathA;
                proj.HdfPathB = hdfPathB;
                proj.PlanNameA = planNameA;
                proj.PlanNameB = planNameB;
                proj.ProName = proName;

                await _settingsRepo.SaveSettings(settings);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HydraulicsPaneView.SaveSettings error: {ex.Message}");
            }
        }

        private const string TwoDOnlyWarningMessage =
            "This plan is a 2D model — RAS Tables show 1D cross-section results, which aren't " +
            "available here. See the Map tab for 2D cell results (max WSE, depth, velocity).";

        private void UpdateProfileWarningVisibility()
        {
            bool onRasTables = ViewModel.TabItem.TabControlViewModel.CurrentTopTab == "RAS Tables";

            // 2D-only plans have no 1D profiles/cross-sections at all — disable the
            // profile picker rather than let it show stale or empty 1D data, and
            // explain why on the tab where that would otherwise be confusing.
            if (_modelDims == ModelDimensions.TwoDOnly)
            {
                cboxProfiles.IsEnabled = false;
                txtProfileWarning.Text = onRasTables ? TwoDOnlyWarningMessage : string.Empty;
                return;
            }

            cboxProfiles.IsEnabled = true;
            txtProfileWarning.Text = onRasTables ? _profileWarningMessage : string.Empty;
        }

        [GeneratedRegex(@"\.p\d+\.hdf$", RegexOptions.IgnoreCase, "en-US")]
        private static partial Regex HdfRegex();
    }
}