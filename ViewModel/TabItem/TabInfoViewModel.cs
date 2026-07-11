using HydroExplorer.Core;
using HydroExplorer.Helpers;
using HydroExplorer.Utils;
using System.IO;
using System.Windows.Input;


namespace HydroExplorer.ViewModel.TabItem
{
    public class TabInfoViewModel : TabViewModelBase
    {

        private string _activeProjPath = string.Empty;
        public string ActiveProjPath
        {
            get => _activeProjPath;
            set { _activeProjPath = value; OnPropertyChanged(); }
        }

        private string _projectName = string.Empty;
        public string ProjName
        {
            get => _projectName;
            set { _projectName = value; OnPropertyChanged(); }
        }

        private string _drainageAreaSqMi = string.Empty;
        public string DrainageAreaSqMi
        {
            get => _drainageAreaSqMi;
            set { _drainageAreaSqMi = value; OnPropertyChanged(); }
        }

        private string _drainageAreaAcre = string.Empty;
        public string DrainageAreaAcre
        {
            get => _drainageAreaAcre;
            set { _drainageAreaAcre = value; OnPropertyChanged(); }
        }

        private string _hmsModel = string.Empty;
        public string HmsModel
        {
            get => _hmsModel;
            set { _hmsModel = value; OnPropertyChanged(); }
        }

        private string _rasModel = string.Empty;
        public string RasModel
        {
            get => _rasModel;
            set { _rasModel = value; OnPropertyChanged(); }
        }

        private string _usgsNumber = string.Empty;
        public string UsgsNumber
        {
            get => _usgsNumber;
            set { _usgsNumber = value; OnPropertyChanged(); }
        }

        private string _usgsStationName = string.Empty;
        public string UsgsStationName
        {
            get => _usgsStationName;
            set { _usgsStationName = value; OnPropertyChanged(); }
        }

        private string _usgsDistanceMiles = string.Empty;
        public string UsgsDistanceMiles
        {
            get => _usgsDistanceMiles;
            set { _usgsDistanceMiles = value; OnPropertyChanged(); }
        }

        private string _hucCode = string.Empty;
        public string HucCode
        {
            get => _hucCode;
            set { _hucCode = value; OnPropertyChanged(); }
        }

        private string _usgsDrainageAreaSqMi = string.Empty;
        public string UsgsDrainageAreaSqMi
        {
            get => _usgsDrainageAreaSqMi;
            set { _usgsDrainageAreaSqMi = value; OnPropertyChanged(); }
        }


        // ── Regression Input (Omega EM) ─────────────────────────────────────
        private string _omegaArea = string.Empty;
        public string OmegaArea
        {
            get => _omegaArea;
            set { _omegaArea = value; OnPropertyChanged(); }
        }

        private string _omegaSlope = string.Empty;
        public string OmegaSlope
        {
            get => _omegaSlope;
            set { _omegaSlope = value; OnPropertyChanged(); }
        }

        private string _omegaPrecip = string.Empty;
        public string OmegaPrecip
        {
            get => _omegaPrecip;
            set { _omegaPrecip = value; OnPropertyChanged(); }
        }

        private string _omegaValue = string.Empty;
        public string OmegaValue
        {
            get => _omegaValue;
            set { _omegaValue = value; OnPropertyChanged(); }
        }

        public ICommand ComputeOmegaCommand { get; }


        private string _header = "Home";
        public override string Header
        {
            get => _header;
            set { _header = value; OnPropertyChanged(nameof(Header)); }
        }

        private RecentProjectEntry? _selectedRecentProject;
        public RecentProjectEntry? SelectedRecentProject
        {
            get => _selectedRecentProject;
            set
            {
                if (_selectedRecentProject == value) return;
                _selectedRecentProject = value;
                OnPropertyChanged(nameof(SelectedRecentProject));

                if (value != null)
                {
                    OpenRecentProject(value);
                    _selectedRecentProject = null;
                    OnPropertyChanged(nameof(SelectedRecentProject));
                }
            }
        }

        public ICommand ClearAllRecentCommand { get; }

        public TabInfoViewModel(IUserSettingsRepo settingsRepo)
        {
            SettingsRepo = settingsRepo;
            _ = ValidateAndLoadRecentProjectsAsync();

            EventBus.ProjPathChanged += async path =>
            {
                await LoadRecentProjectsAsync();
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    PopulateProjectInfo(path));
            };

            EventBus.ProjPathSelected += async path =>
            {
                await LoadRecentProjectsAsync();
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    PopulateProjectInfo(path));
            };

            EventBus.RunPathSelected += async path =>
            {
                await LoadRecentProjectsAsync();
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    HmsModel = Path.GetFileName(path));
            };

            EventBus.AppLoaded += async settings =>
            {
                await LoadRecentProjectsAsync();
                string projPath = settings.LastProjPath;
                if (!string.IsNullOrEmpty(projPath))
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        PopulateProjectInfo(projPath));
            };

            // BNDY.shp may not exist yet when PopulateProjectInfo first runs its gage
            // lookup (MapOverView's export can still be in progress). Once GeometryPathsResolved
            // confirms a real, existing BNDY.shp, re-check — GetNearestGageWithStatusAsync
            // itself is now idempotent (fetches at most once ever per project), so this
            // is safe to call repeatedly with no risk of redundant network calls.
            EventBus.GeometryPathsResolved += async (pathSubBasins, pathRiver, pathXS, pathBNDY) =>
            {
                if (string.IsNullOrEmpty(pathBNDY) || !File.Exists(pathBNDY)) return;

                try
                {
                    var settings = await SettingsRepo!.GetSettingsFresh();
                    string projPath = settings.LastProjPath;

                    var (gage, gageStatus) = await USGSReader.GetNearestGageWithStatusAsync(projPath);

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (gage is not null)
                        {
                            UsgsNumber = gage.SiteNo;
                            UsgsStationName = gage.SiteName;
                            UsgsDistanceMiles = $"{gage.DistanceMiles:F1} mi";
                            HucCode = gage.HucCode;
                            UsgsDrainageAreaSqMi = gage.DrainageAreaSqMi.HasValue
                                ? $"{gage.DrainageAreaSqMi:F1} sq mi" : "N/A";
                        }
                        else if (gageStatus == GageLookupStatus.NoGageInRange)
                        {
                            UsgsNumber = string.Empty;
                            UsgsDistanceMiles = string.Empty;
                            HucCode = string.Empty;
                            UsgsDrainageAreaSqMi = string.Empty;
                            UsgsStationName = "No gage within 25 mi";
                        }
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"GeometryPathsResolved gage re-fetch failed — {ex.Message}");
                }
            };

            ClearAllRecentCommand = new RelayCommand(async () => await ClearRecentProjectsAsync());
            ComputeOmegaCommand = new RelayCommand(async () => await ComputeOmegaAsync());
        }


        private async Task ValidateAndLoadRecentProjectsAsync()
        {
            var settings = await SettingsRepo!.GetSettings();
            bool dirty = false;

            var deadProjects = settings.Projects.Keys
                .Where(k => !File.Exists(k))
                .ToList();

            foreach (var key in deadProjects)
            {
                settings.Projects.Remove(key);
                dirty = true;
            }

            if (dirty)
                await SettingsRepo.SaveSettings(settings);

            await LoadRecentProjectsAsync();
        }


        private CancellationTokenSource? _openProjectCts;

        private async void OpenRecentProject(RecentProjectEntry entry)
        {
            _openProjectCts?.Cancel();
            _openProjectCts = new CancellationTokenSource();
            var token = _openProjectCts.Token;

            try
            {
                await Task.Delay(200, token);
                if (token.IsCancellationRequested) return;

                var settings = await SettingsRepo!.GetSettings();
                if (!settings.Projects.TryGetValue(entry.Key, out var proj))
                {
                    // Entry no longer exists — refresh the list to drop it.
                    await LoadRecentProjectsAsync();
                    return;
                }

                bool isRas = entry.Key.EndsWith(".prj", StringComparison.OrdinalIgnoreCase)
                          || entry.Key.EndsWith(".rasmap", StringComparison.OrdinalIgnoreCase);

                if (!isRas)
                {
                    await OpenRecentHmsProject(entry.Key, proj);
                    return;
                }

                if (!File.Exists(entry.Key))
                {
                    await LoadRecentProjectsAsync();
                    return;
                }

                proj.LastOpened = DateTime.Now;
                settings.LastProjPath = entry.Key;
                settings.ProjPath = entry.Key;
                settings.ProjName = proj.ProjName;
                await SettingsRepo.SaveSettings(settings);

                EventBus.PublishProjPath(entry.Key);
                EventBus.PublishProjPathChanged(entry.Key);
                EventBus.PublishRecentProjectSelected(entry.Key);

                await LoadRecentProjectsAsync(entry.Key);

                // HmsPath is already stored directly on this project's own
                // ProjectSettings (set by SaveHmsPathAsync when the .hms was
                // originally selected for this ProjRoot) — no separate lookup
                // needed the way earlier versions searched other Projects entries.
                if (!string.IsNullOrEmpty(proj.HmsPath) && File.Exists(proj.HmsPath))
                    EventBus.PublishRunPath(proj.HmsPath);

                await LoadRecentProjectsAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OpenRecentProject] ERROR: {ex.Message}");
            }
        }

        private async Task OpenRecentHmsProject(string key, ProjectSettings proj)
        {
            if (string.IsNullOrEmpty(proj.HmsPath) || !File.Exists(proj.HmsPath))
            {
                await LoadRecentProjectsAsync();
                return;
            }

            var settings = await SettingsRepo!.GetSettings();
            proj.LastOpened = DateTime.Now;
            settings.LastProjPath = key;
            settings.ProjPath = key;
            settings.ProjName = proj.ProjName;
            await SettingsRepo.SaveSettings(settings);

            EventBus.PublishRunPath(proj.HmsPath);
            EventBus.PublishProjPathChanged(key);
            EventBus.PublishRecentProjectSelected(key);

            await LoadRecentProjectsAsync(key);
        }





        private async Task ClearRecentProjectsAsync()
        {
            var settings = await SettingsRepo!.GetSettings();
            settings.Projects.Clear();
            settings.LastProjPath = string.Empty;
            settings.ProjPath = string.Empty;
            await SettingsRepo.SaveSettings(settings);
            await LoadRecentProjectsAsync();
        }


        private async Task RemoveRecentProjectAsync(string dirPath)
        {
            var settings = await SettingsRepo!.GetSettings();

            // Projects keys are full file paths — remove any whose directory matches
            var projKeysToRemove = settings.Projects.Keys
                .Where(k => Path.GetDirectoryName(k)
                    ?.Equals(dirPath, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            foreach (var key in projKeysToRemove)
                settings.Projects.Remove(key);

            await SettingsRepo.SaveSettings(settings);
            await LoadRecentProjectsAsync();
        }


        private void PopulateProjectInfo(string projPath)
        {
            string normalizedPath = PathHelpers.NormalizeProjKey(projPath);
            string dir = Path.GetDirectoryName(normalizedPath) ?? string.Empty;

            if (!Directory.Exists(dir))
            {
                return;
            }

            string rasModelName = string.Empty;
            rasModelName = Path.GetFileName(normalizedPath);

            ProjName = string.Empty;
            RasModel = rasModelName;
            HmsModel = string.Empty;
            DrainageAreaSqMi = string.Empty;
            DrainageAreaAcre = string.Empty;
            UsgsNumber = string.Empty;
            UsgsStationName = string.Empty;
            UsgsDistanceMiles = string.Empty;
            HucCode = string.Empty;
            UsgsDrainageAreaSqMi = string.Empty;
            OmegaArea = string.Empty;
            OmegaSlope = string.Empty;
            OmegaPrecip = string.Empty;
            OmegaValue = string.Empty;

            _ = Task.Run(async () =>
            {
                var settings = await SettingsRepo!.GetSettings();
                settings.Projects.TryGetValue(settings.LastProjPath, out var proj);

                string hmsName = string.Empty;
                string? hmsPathResolved = null;

                if (!string.IsNullOrEmpty(proj?.ProjName))
                    ProjName = proj?.ProjName;

                if (!string.IsNullOrEmpty(proj?.HmsPath) && File.Exists(proj.HmsPath))
                {
                    hmsName = Path.GetFileName(proj.HmsPath);
                    hmsPathResolved = proj.HmsPath;
                }

                if (string.IsNullOrEmpty(hmsName))
                {
                    var matchingRun = settings.Projects
                        .Where(kv => kv.Value.ProjRoot.Equals(proj?.ProjRoot, StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrEmpty(kv.Value.HmsPath)
                            && File.Exists(kv.Value.HmsPath))
                        .Select(kv => kv.Value.HmsPath)
                        .FirstOrDefault();

                    if (!string.IsNullOrEmpty(matchingRun))
                    {
                        hmsName = Path.GetFileName(matchingRun);
                        hmsPathResolved = matchingRun;
                    }
                }

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    HmsModel = hmsName;
                    LoadOmegaInputs(proj);
                });

                // ── Subbasin area (sum of all Subbasin: Area: in the .hms
                // project's first Basin: block's .basin file). Left blank
                // (already cleared above) if no .hms is resolved.
                if (!string.IsNullOrEmpty(hmsPathResolved))
                {
                    double? totalAreaSqMi = HecHmsBasinReader.GetTotalSubbasinAreaSqMi(hmsPathResolved);

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (totalAreaSqMi.HasValue)
                        {
                            DrainageAreaSqMi = totalAreaSqMi.Value.ToString("F2");
                            DrainageAreaAcre = (totalAreaSqMi.Value * 640.0).ToString("F1");
                        }
                        else
                        {
                            DrainageAreaSqMi = string.Empty;
                            DrainageAreaAcre = string.Empty;
                        }
                    });
                }

                // ── USGS nearest gage ─────────────────────────────────────
                try
                {
                    var (gage, gageStatus) = await USGSReader.GetNearestGageWithStatusAsync(settings.LastProjPath);

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (gage is not null)
                        {
                            UsgsNumber = gage.SiteNo;
                            UsgsStationName = gage.SiteName;
                            UsgsDistanceMiles = $"{gage.DistanceMiles:F1} mi";
                            HucCode = gage.HucCode;
                            UsgsDrainageAreaSqMi = gage.DrainageAreaSqMi.HasValue
                                ? $"{gage.DrainageAreaSqMi:F1} sq mi" : "N/A";
                        }
                        else
                        {
                            UsgsNumber = string.Empty;
                            UsgsDistanceMiles = string.Empty;
                            HucCode = string.Empty;
                            UsgsDrainageAreaSqMi = string.Empty;

                            UsgsStationName = gageStatus switch
                            {
                                GageLookupStatus.NoCentroid => "Boundary not defined",
                                GageLookupStatus.NoGageInRange => "No gage within 25 mi",
                                _ => string.Empty
                            };
                        }
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"PopulateProjectInfo: USGS gage lookup failed — {ex.Message}");
                }
            });
        }


        // ── Regression Input (Omega EM) ─────────────────────────────────────

        private async Task ComputeOmegaAsync()
        {
            if (!double.TryParse(OmegaArea, out double area) ||
                !double.TryParse(OmegaSlope, out double slope) ||
                !double.TryParse(OmegaPrecip, out double precip) ||
                !double.TryParse(OmegaValue, out double omega))
            {
                System.Windows.MessageBox.Show(
                    "Enter valid numeric values for all four regression inputs.",
                    "Invalid Input", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            try
            {
                var settings = await SettingsRepo!.GetSettingsFresh();
                if (!string.IsNullOrEmpty(settings.LastProjPath) &&
                    settings.Projects.TryGetValue(settings.LastProjPath, out var proj))
                {
                    proj.OmegaArea = area;
                    proj.OmegaSlope = slope;
                    proj.OmegaPrecip = precip;
                    proj.OmegaValue = omega;
                    await SettingsRepo.SaveSettings(settings);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ComputeOmegaAsync save error: {ex.Message}");
            }

            EventBus.PublishOmegaInputChanged(area, slope, precip, omega);
        }

        /// <summary>
        /// Restores saved regression inputs for the given project and, if all
        /// four are present, recomputes immediately so the Info tab's Omega
        /// plot reflects them without requiring the user to click Compute
        /// again after reopening the project.
        /// </summary>
        private void LoadOmegaInputs(ProjectSettings? proj)
        {
            if (proj is null)
            {
                OmegaArea = OmegaSlope = OmegaPrecip = OmegaValue = string.Empty;
                return;
            }

            OmegaArea = proj.OmegaArea?.ToString() ?? string.Empty;
            OmegaSlope = proj.OmegaSlope?.ToString() ?? string.Empty;
            OmegaPrecip = proj.OmegaPrecip?.ToString() ?? string.Empty;
            OmegaValue = proj.OmegaValue?.ToString() ?? string.Empty;

            if (proj.OmegaArea.HasValue && proj.OmegaSlope.HasValue &&
                proj.OmegaPrecip.HasValue && proj.OmegaValue.HasValue)
            {
                EventBus.PublishOmegaInputChanged(
                    proj.OmegaArea.Value, proj.OmegaSlope.Value,
                    proj.OmegaPrecip.Value, proj.OmegaValue.Value);
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