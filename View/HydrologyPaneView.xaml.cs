using HydroExplorer.Helpers;
using HydroExplorer.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace HydroExplorer.View
{
    public partial class HydrologyPaneView : UserControl
    {
        private readonly IUserSettingsRepo _settingsRepo;
        private string _lastProjPath = string.Empty;

        public HydrologyPaneView()
        {
            InitializeComponent();
            _settingsRepo = App.ServiceProvider.GetRequiredService<IUserSettingsRepo>();

            EventBus.AppLoaded += OnAppLoaded;

            EventBus.RunPathSelected += path =>
            {
                Dispatcher.Invoke(() => PopulateHmsRunComboBox(path));
            };

            EventBus.ProjPathChanged += async path =>
            {
                _lastProjPath = PathHelpers.NormalizeProjKey(path);

                await Dispatcher.InvokeAsync(() =>
                {
                    txtBoxHmsPath.Text = string.Empty;
                    txtBoxHmsPathB.Text = string.Empty;
                    cboxHmsRun.ItemsSource = (List<string>)["None"];
                    cboxHmsRunB.ItemsSource = (List<string>)["None"];
                });

                var settings = await _settingsRepo.GetSettingsFresh();

                await Dispatcher.InvokeAsync(() =>
                {
                    if (!string.IsNullOrEmpty(_lastProjPath) &&
                        settings.Projects.TryGetValue(_lastProjPath, out var proj) &&
                        !string.IsNullOrEmpty(proj.HmsPath))
                    {
                        PopulateHmsRunComboBox(proj.HmsPath);
                    }
                });
            };

            EventBus.HmsPathChanged += path =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (!string.IsNullOrEmpty(path))
                        PopulateHmsRunComboBox(path);
                });
            };

            cboxHmsRun.SelectionChanged += async (s, e) =>
            {
                if (_isLoadingHmsRun) return;
                if (cboxHmsRun.SelectedItem is not HmsRunDisplayItem selected) return;
                if (string.IsNullOrEmpty(_currentHmsProjKey)) return;

                string plainName = selected.DisplayName
                    .TrimStart('⚠', ' ')
                    .Split(" - needs recompute")[0];

                try
                {
                    var settings = await _settingsRepo.GetSettingsFresh();
                    if (settings.Projects.TryGetValue(_currentHmsProjKey, out var proj))
                    {
                        proj.SelectedHmsRun = plainName;
                        await _settingsRepo.SaveSettings(settings);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"cboxHmsRun.SelectionChanged save error: {ex.Message}");
                }

                // The selected run's own DSS File: line (from the .run file) is
                // authoritative — different runs/events can reference different
                // .dss files under the same .hms project.
                var matchingRun = _hmsRuns.FirstOrDefault(r =>
                    string.Equals(r.Name, plainName, StringComparison.OrdinalIgnoreCase));

                if (matchingRun != null && !string.IsNullOrEmpty(matchingRun.DssFile))
                {
                    string dssPath = Path.Combine(_hmsDir, matchingRun.DssFile);
                    if (File.Exists(dssPath))
                    {
                        EventBus.PublishDssRunSelected(dssPath, plainName);
                        await SaveDssPath(dssPath);
                    }
                }
            };

            EventBus.ProjPathSelected += path =>
            {
                _lastProjPath = PathHelpers.NormalizeProjKey(path);
            };

            cboxHmsRunB.SelectionChanged += async (s, e) =>
            {
                if (_isLoadingHmsRunB) return;
                if (cboxHmsRunB.SelectedItem is not HmsRunDisplayItem selected) return;
                if (string.IsNullOrEmpty(_currentHmsProjKey)) return;

                string plainName = selected.DisplayName
                    .TrimStart('⚠', ' ')
                    .Split(" - needs recompute")[0];

                try
                {
                    var settings = await _settingsRepo.GetSettingsFresh();
                    if (settings.Projects.TryGetValue(_currentHmsProjKey, out var proj))
                    {
                        proj.SelectedHmsRunB = plainName;
                        await _settingsRepo.SaveSettings(settings);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"cboxHmsRunB.SelectionChanged save error: {ex.Message}");
                }

                var matchingRun = _hmsRuns.FirstOrDefault(r =>
                    string.Equals(r.Name, plainName, StringComparison.OrdinalIgnoreCase));

                if (matchingRun != null && !string.IsNullOrEmpty(matchingRun.DssFile))
                {
                    string dssPath = Path.Combine(_hmsDir, matchingRun.DssFile);
                    if (File.Exists(dssPath))
                    {
                        EventBus.PublishDssRunBSelected(dssPath, plainName);
                        await SaveDssPath(dssPath, isSlotB: true);
                    }
                }
            };
        }

        private void OnAppLoaded(UserSettings settings)
        {
            _lastProjPath = PathHelpers.NormalizeProjKey(settings.LastProjPath);

            Dispatcher.Invoke(() =>
            {
                if (!string.IsNullOrEmpty(_lastProjPath) &&
                    settings.Projects.TryGetValue(_lastProjPath, out var proj) &&
                    !string.IsNullOrEmpty(proj.HmsPath))
                {
                    PopulateHmsRunComboBox(proj.HmsPath);
                }
            });
        }

        private async void BrowseHmsProject_Click(object sender, RoutedEventArgs e)
        {
            var settings = await _settingsRepo.GetSettings();
            string initialDir = string.Empty;
            if (!string.IsNullOrEmpty(_lastProjPath) &&
                settings.Projects.TryGetValue(_lastProjPath, out var proj))
                initialDir = proj.ProjRoot;

            var dialog = new OpenFileDialog
            {
                Title = "Select HMS Project File",
                Filter = "HMS Project Files (*.hms)|*.hms|All Files (*.*)|*.*",
                InitialDirectory = Directory.Exists(initialDir) ? initialDir : string.Empty
            };
            if (dialog.ShowDialog() != true) return;
            string path = dialog.FileName;
            PopulateHmsRunComboBox(path);
            EventBus.PublishRunPath(path);
            await SaveHmsPath(path);
        }


        public class HmsRunDisplayItem
        {
            public string DisplayName { get; init; } = string.Empty;
            public bool IsStale { get; init; }
            public override string ToString() => DisplayName;
        }

        private record HmsRunInfo(
            string Name,
            string DssFile,
            DateTime? LastExecution
        );

        private List<HmsRunInfo> _hmsRuns = [];
        private string _hmsDir = string.Empty;

        private bool _isLoadingHmsRun = false;
        private bool _isLoadingHmsRunB = false;

        private string _currentHmsProjKey = string.Empty;


        private async void PopulateHmsRunComboBox(string hmsPath)
        {
            if (string.IsNullOrEmpty(hmsPath) || !File.Exists(hmsPath)) return;

            txtBoxHmsPath.Text = Path.GetFileName(hmsPath);
            txtBoxHmsPathB.Text = Path.GetFileName(hmsPath);

            try
            {
                string runPath = Path.ChangeExtension(hmsPath, ".run");
                if (!File.Exists(runPath))
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"PopulateHmsRunComboBox: no .run file found at '{runPath}'.");
                    cboxHmsRun.ItemsSource = (List<string>)["None"];
                    return;
                }

                DateTime hmsModified = File.GetLastWriteTime(hmsPath);
                _hmsDir = Path.GetDirectoryName(hmsPath) ?? string.Empty;
                _hmsRuns = ParseRunFile(runPath);

                var displayItems = _hmsRuns.Select(r =>
                {
                    bool upToDate = r.LastExecution.HasValue && r.LastExecution.Value > hmsModified;
                    return new HmsRunDisplayItem
                    {
                        DisplayName = upToDate ? r.Name : $"⚠ {r.Name} - needs recompute",
                        IsStale = !upToDate
                    };
                }).ToList();

                cboxHmsRun.ItemsSource = displayItems.Count > 0
                    ? (IEnumerable<object>)displayItems
                    : (IEnumerable<object>)new List<HmsRunDisplayItem>
                        { new() { DisplayName = "None", IsStale = false } };

                cboxHmsRunB.ItemsSource = displayItems.Count > 0
                    ? (IEnumerable<object>)displayItems
                    : (IEnumerable<object>)new List<HmsRunDisplayItem>
                        { new() { DisplayName = "None", IsStale = false } };

                var settings = await _settingsRepo.GetSettings();
                string resolvedProjKey = settings.Projects
                    .Where(kv => string.Equals(kv.Value.HmsPath, hmsPath, StringComparison.OrdinalIgnoreCase))
                    .Select(kv => kv.Key)
                    .FirstOrDefault() ?? _lastProjPath;

                _currentHmsProjKey = resolvedProjKey;

                if (displayItems.Count > 0)
                {
                    string savedRun = !string.IsNullOrEmpty(resolvedProjKey) &&
                        settings.Projects.TryGetValue(resolvedProjKey, out var proj)
                        ? proj.SelectedHmsRun
                        : string.Empty;

                    int restoreIndex = !string.IsNullOrEmpty(savedRun)
                        ? displayItems.FindIndex(d => d.DisplayName.Contains(savedRun, StringComparison.OrdinalIgnoreCase))
                        : -1;

                    _isLoadingHmsRun = true;
                    try
                    {
                        cboxHmsRun.SelectedIndex = restoreIndex >= 0 ? restoreIndex : 0;
                    }
                    finally
                    {
                        _isLoadingHmsRun = false;
                    }

                    // SelectionChanged is suppressed above (by design, to avoid
                    // re-saving on a programmatic restore) — but that also means
                    // it never publishes DssRunSelected, so charts never reload
                    // on project switch. Resolve and publish explicitly here.
                    if (cboxHmsRun.SelectedItem is HmsRunDisplayItem restoredItem)
                    {
                        string restoredName = restoredItem.DisplayName
                            .TrimStart('⚠', ' ')
                            .Split(" - needs recompute")[0];

                        var matchingRestoredRun = _hmsRuns.FirstOrDefault(r =>
                            string.Equals(r.Name, restoredName, StringComparison.OrdinalIgnoreCase));

                        if (matchingRestoredRun != null && !string.IsNullOrEmpty(matchingRestoredRun.DssFile))
                        {
                            string restoredDssPath = Path.Combine(_hmsDir, matchingRestoredRun.DssFile);
                            if (File.Exists(restoredDssPath))
                                EventBus.PublishDssRunSelected(restoredDssPath, restoredName);
                        }
                    }
                }

                // Run B restore — same run list, independent selection.
                if (displayItems.Count > 0)
                {
                    string savedRunB = !string.IsNullOrEmpty(resolvedProjKey) &&
                        settings.Projects.TryGetValue(resolvedProjKey, out var projB)
                        ? projB.SelectedHmsRunB
                        : string.Empty;

                    int restoreIndexB = !string.IsNullOrEmpty(savedRunB)
                        ? displayItems.FindIndex(d => d.DisplayName.Contains(savedRunB, StringComparison.OrdinalIgnoreCase))
                        : -1;

                    _isLoadingHmsRunB = true;
                    try
                    {
                        cboxHmsRunB.SelectedIndex = restoreIndexB >= 0 ? restoreIndexB : -1; // no default — B stays unset unless previously chosen
                    }
                    finally
                    {
                        _isLoadingHmsRunB = false;
                    }

                    if (cboxHmsRunB.SelectedItem is HmsRunDisplayItem restoredItemB)
                    {
                        string restoredNameB = restoredItemB.DisplayName
                            .TrimStart('⚠', ' ')
                            .Split(" - needs recompute")[0];

                        var matchingRestoredRunB = _hmsRuns.FirstOrDefault(r =>
                            string.Equals(r.Name, restoredNameB, StringComparison.OrdinalIgnoreCase));

                        if (matchingRestoredRunB != null && !string.IsNullOrEmpty(matchingRestoredRunB.DssFile))
                        {
                            string restoredDssPathB = Path.Combine(_hmsDir, matchingRestoredRunB.DssFile);
                            if (File.Exists(restoredDssPathB))
                                EventBus.PublishDssRunBSelected(restoredDssPathB, restoredNameB);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PopulateHmsRunComboBox error: {ex.Message}");
                cboxHmsRun.ItemsSource = (List<string>)["None"];
            }
        }

        /// <summary>
        /// Parses Run: blocks from a HEC-HMS .run file. Each block looks like:
        ///   Run: &lt;name&gt;
        ///        Last Modified Date: 20 June 2026
        ///        Last Modified Time: 07:11:08
        ///        Last Execution Date: 20 June 2026
        ///        Last Execution Time: 06:53:37
        ///        DSS File: 100_year_no_reduction.dss
        ///   End:
        /// </summary>
        private static List<HmsRunInfo> ParseRunFile(string runPath)
        {
            var runs = new List<HmsRunInfo>();
            string? currentName = null;
            string? dssFile = null;
            string? execDate = null;
            string? execTime = null;

            foreach (var raw in File.ReadLines(runPath))
            {
                string line = raw.Trim();

                if (line.StartsWith("Run:", StringComparison.OrdinalIgnoreCase))
                {
                    currentName = line[4..].Trim();
                    dssFile = null;
                    execDate = null;
                    execTime = null;
                }
                else if (line.StartsWith("Last Execution Date:", StringComparison.OrdinalIgnoreCase))
                {
                    execDate = line["Last Execution Date:".Length..].Trim();
                }
                else if (line.StartsWith("Last Execution Time:", StringComparison.OrdinalIgnoreCase))
                {
                    execTime = line["Last Execution Time:".Length..].Trim();
                }
                else if (line.StartsWith("DSS File:", StringComparison.OrdinalIgnoreCase))
                {
                    dssFile = line["DSS File:".Length..].Trim();
                }
                else if (line.Equals("End:", StringComparison.OrdinalIgnoreCase) &&
                         currentName != null)
                {
                    DateTime? execDt = null;
                    if (!string.IsNullOrEmpty(execDate))
                    {
                        string combined = string.IsNullOrEmpty(execTime)
                            ? execDate
                            : $"{execDate} {execTime}";
                        if (DateTime.TryParse(combined,
                                System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.None, out var dt))
                            execDt = dt;
                    }

                    runs.Add(new HmsRunInfo(currentName, dssFile ?? string.Empty, execDt));
                    currentName = null;
                }
            }

            return runs;
        }

        private async Task SaveHmsPath(string path)
        {
            try
            {
                var settings = await _settingsRepo.GetSettingsFresh();

                string projectsRoot = ProjectsFolder.Resolve(settings);

                var current = new DirectoryInfo(Path.GetDirectoryName(path) ?? string.Empty);
                while (current?.Parent != null &&
                       !ProjectsFolder.IsRoot(current.Parent.FullName, projectsRoot))
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

        private async Task SaveDssPath(string path, bool isSlotB = false)
        {
            try
            {
                var settings = await _settingsRepo.GetSettingsFresh();
                if (string.IsNullOrEmpty(_lastProjPath))
                    _lastProjPath = PathHelpers.NormalizeProjKey(settings.LastProjPath);

                if (!string.IsNullOrEmpty(_lastProjPath) &&
                    settings.Projects.TryGetValue(_lastProjPath, out var proj))
                {
                    if (isSlotB) proj.DssPathB = path;
                    else proj.DssPath = path;
                    await _settingsRepo.SaveSettings(settings);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SaveDssPath error: {ex.Message}");
            }
        }


        private void TreeViewItem_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
            => e.Handled = true;

    }
}