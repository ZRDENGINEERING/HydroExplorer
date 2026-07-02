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

        private string _lastPublishedDssRun = string.Empty;


        public HydrologyPaneView()
        {
            InitializeComponent();
            _settingsRepo = App.ServiceProvider.GetRequiredService<IUserSettingsRepo>();

            EventBus.AppLoaded += OnAppLoaded;

            EventBus.RunPathSelected += path =>
            {
                Dispatcher.Invoke(() =>
                {
                    txtHmsProjectPath.Text = path;
                    PopulateHmsRunComboBox(path);
                    PopulateDssRunComboBox(path);
                });
            };

            EventBus.ProjPathChanged += async path =>
            {
                _lastProjPath = PathHelpers.NormalizeProjKey(path);

                await Dispatcher.InvokeAsync(() =>
                {
                    txtHmsProjectPath.Text = string.Empty;
                    txtDssFilePath.Text = string.Empty;
                    cboxHmsRun.ItemsSource = (List<string>)["None"];
                    cboxDssRun.ItemsSource = (List<string>)["None"];
                });

                await Task.Delay(800);

                var settings = await _settingsRepo.GetSettingsFresh();
                settings.Projects.TryGetValue(_lastProjPath, out var dbgProj);

                await Dispatcher.InvokeAsync(() =>
                {
                    if (!string.IsNullOrEmpty(_lastProjPath) &&
                        settings.Projects.TryGetValue(_lastProjPath, out var proj))
                    {
                        if (!string.IsNullOrEmpty(proj.HmsPath))
                        {
                            txtHmsProjectPath.Text = proj.HmsPath;
                            PopulateHmsRunComboBox(proj.HmsPath);
                        }

                        var dssPath = proj.DssPath ?? string.Empty;
                        if (!string.IsNullOrEmpty(dssPath))
                        {
                            txtDssFilePath.Text = dssPath;
                            PopulateDssRunComboBox(dssPath);
                        }
                    }
                });
            };

            EventBus.HmsPathChanged += path =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (!string.IsNullOrEmpty(path))
                    {
                        txtHmsProjectPath.Text = path;
                        PopulateHmsRunComboBox(path);
                        PopulateDssRunComboBox(path);
                    }
                });
            };


            cboxDssRun.SelectionChanged += (s, e) =>
            {
                if (cboxDssRun.SelectedItem is string runName &&
                    !string.IsNullOrEmpty(txtDssFilePath.Text))
                    EventBus.PublishDssRunSelected(txtDssFilePath.Text, runName);
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

                // Re-resolve DSS run for the newly selected HMS run — PopulateDssRunComboBox
                // filters by _hmsRuns' LastExecution/DssFile per run, so this picks up
                // whichever .dss file actually belongs to `plainName`, not whatever was
                // showing for the previously selected run.
                var matchingRun = _hmsRuns.FirstOrDefault(r =>
                    string.Equals(r.Name, plainName, StringComparison.OrdinalIgnoreCase));

                if (matchingRun != null && !string.IsNullOrEmpty(matchingRun.DssFile))
                {
                    string dssPath = Path.Combine(_hmsDir, matchingRun.DssFile);
                    if (File.Exists(dssPath))
                        EventBus.PublishDssRunSelected(dssPath, plainName);
                }
            };



            EventBus.ProjPathSelected += path =>
            {
                _lastProjPath = PathHelpers.NormalizeProjKey(path);
            };
        }

        private void OnAppLoaded(UserSettings settings)
        {
            _lastProjPath = PathHelpers.NormalizeProjKey(settings.LastProjPath);

            Dispatcher.Invoke(() =>
            {
                if (!string.IsNullOrEmpty(_lastProjPath) &&
                    settings.Projects.TryGetValue(_lastProjPath, out var proj))
                {
                    if (!string.IsNullOrEmpty(proj.HmsPath))
                    {
                        txtHmsProjectPath.Text = proj.HmsPath;
                        PopulateHmsRunComboBox(proj.HmsPath);
                    }

                    if (!string.IsNullOrEmpty(proj?.DssPath))
                        txtDssFilePath.Text = proj.DssPath;
                }
            });

            if (!string.IsNullOrEmpty(_lastProjPath) &&
                settings.Projects.TryGetValue(_lastProjPath, out var p) &&
                !string.IsNullOrEmpty(p.DssPath))
                PopulateDssRunComboBox(p.DssPath);
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
            txtHmsProjectPath.Text = path;
            PopulateHmsRunComboBox(path);
            EventBus.PublishRunPath(path);
            await SaveHmsPath(path);
        }

        private async void BrowseDss_Click(object sender, RoutedEventArgs e)
        {
            var settings = await _settingsRepo.GetSettings();
            string initialDir = string.Empty;
            if (!string.IsNullOrEmpty(_lastProjPath) &&
                settings.Projects.TryGetValue(_lastProjPath, out var proj))
                initialDir = proj.ProjRoot;

            var dialog = new OpenFileDialog
            {
                Title = "Select DSS File",
                Filter = "DSS Files (*.dss)|*.dss|All Files (*.*)|*.*",
                InitialDirectory = Directory.Exists(initialDir) ? initialDir : string.Empty
            };
            if (dialog.ShowDialog() != true) return;
            string path = dialog.FileName;
            txtDssFilePath.Text = path;
            PopulateDssRunComboBox(path);
            await SaveDssPath(path);
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

        private string _currentHmsProjKey = string.Empty;



        private async void PopulateHmsRunComboBox(string hmsPath)
        {
            if (string.IsNullOrEmpty(hmsPath) || !File.Exists(hmsPath)) return;

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

                // Resolve the actual project key for THIS hmsPath directly from
                // settings, instead of trusting _lastProjPath — that field is only
                // updated by ProjPathChanged/ProjPathSelected, and can lag behind
                // RunPathSelected/HmsPathChanged calling this method for a different
                // project's .hms file, causing saves to land under the wrong project
                // (observed: OPR's run bleeding into Austin's SelectedHmsRun).
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
                }

                // Refresh DSS list now that run info is loaded.
                PopulateDssRunComboBox(hmsPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PopulateHmsRunComboBox error: {ex.Message}");
                cboxHmsRun.ItemsSource = (List<string>)["None"];
            }
        }




        private async void PopulateDssRunComboBox(string dssPath)
        {
            // DSS Results — only .dss files whose corresponding run has been
            // executed after the .hms file was last modified (i.e. up-to-date),
            // and whose .dss file actually exists in the directory.
            string dir = !string.IsNullOrEmpty(_hmsDir) ? _hmsDir
                : File.Exists(dssPath) ? Path.GetDirectoryName(dssPath) ?? string.Empty
                : Directory.Exists(dssPath) ? dssPath : string.Empty;

            if (string.IsNullOrEmpty(dir))
            {
                cboxDssRun.ItemsSource = (List<string>)["None"];
                return;
            }

            try
            {
                string hmsPath = !string.IsNullOrEmpty(_hmsDir)
                    ? Directory.GetFiles(_hmsDir, "*.hms").FirstOrDefault() ?? string.Empty
                    : string.Empty;
                DateTime hmsModified = File.Exists(hmsPath)
                    ? File.GetLastWriteTime(hmsPath)
                    : DateTime.MinValue;

                var upToDateDss = await Task.Run(() =>
                {
                    // If we have parsed run info, use it to filter; otherwise fall
                    // back to listing all .dss files in the directory.
                    if (_hmsRuns.Count > 0)
                    {
                        return _hmsRuns
                            .Where(r => r.LastExecution.HasValue && r.LastExecution.Value > hmsModified)
                            .Select(r => r.DssFile)
                            .Where(f => !string.IsNullOrEmpty(f) &&
                                        File.Exists(Path.Combine(dir, f)))
                            .OrderBy(f => f)
                            .ToList();
                    }

                    return Directory.GetFiles(dir, "*.dss", SearchOption.TopDirectoryOnly)
                        .Select(Path.GetFileName)
                        .OrderBy(f => f)
                        .ToList();
                });

                Application.Current.Dispatcher.Invoke(() =>
                {
                    cboxDssRun.ItemsSource = upToDateDss.Count > 0
                        ? upToDateDss
                        : (List<string>)["None"];

                    if (upToDateDss.Count > 0)
                    {
                        cboxDssRun.SelectedIndex = 0;

                        string fullDssPath = Path.Combine(dir, upToDateDss[0]);
                        string candidateKey = $"{fullDssPath}|{upToDateDss[0]}";

                        // Same de-dup guard as before — avoids re-firing/re-saving on
                        // every cascade trigger (ProjPathChanged, HmsPathChanged,
                        // RunPathSelected all rebuild this combo box independently).
                        if (candidateKey != _lastPublishedDssRun)
                        {
                            _lastPublishedDssRun = candidateKey;
                            EventBus.PublishDssRunSelected(fullDssPath, upToDateDss[0]);

                            // Persist the auto-discovered default the same way
                            // Hydraulics auto-selects and saves Plan Name A — the user
                            // shouldn't have to manually browse just to get the first
                            // available DSS run wired up.
                            _ = SaveDssPath(fullDssPath);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PopulateDssRunComboBox error: {ex.Message}");
                cboxDssRun.ItemsSource = (List<string>)["None"];
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
                    proj = settings.Projects[activeKey] = new ProjectSettings();

                proj.ProjName = current?.Name ?? string.Empty;
                proj.ProjRoot = projRoot;
                proj.HmsPath = path;
                proj.LastOpened = DateTime.Now;

                settings.LastProjPath = activeKey;
                settings.ProjPath = activeKey;

                await _settingsRepo.SaveSettings(settings);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HydrologyPaneView.SaveHmsPath error: {ex.Message}");
            }
        }

        private async Task SaveDssPath(string path)
        {
            try
            {
                var settings = await _settingsRepo.GetSettingsFresh();
                if (string.IsNullOrEmpty(_lastProjPath))
                    _lastProjPath = PathHelpers.NormalizeProjKey(settings.LastProjPath);

                System.Diagnostics.Debug.WriteLine($"[SaveDssPath] path={path}");
                System.Diagnostics.Debug.WriteLine($"[SaveDssPath] _lastProjPath={_lastProjPath}");

                if (!string.IsNullOrEmpty(_lastProjPath) &&
                    settings.Projects.TryGetValue(_lastProjPath, out var proj))
                {
                    proj.DssPath = path;
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