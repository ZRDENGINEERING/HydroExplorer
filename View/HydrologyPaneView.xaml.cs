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
                txtHmsProjectPath.Text = path;
                PopulateHmsRunComboBox(path);
            };

            cboxDssRun.SelectionChanged += (s, e) =>
            {
                if (cboxDssRun.SelectedItem is string runName &&
                    !string.IsNullOrEmpty(txtDssFilePath.Text))
                    EventBus.PublishDssRunSelected(txtDssFilePath.Text, runName);
            };

            EventBus.ProjPathSelected += path =>
            {
                _lastProjPath = TreeViewControl.NormalizeProjKey(path);
            };
        }

        private void OnAppLoaded(UserSettings settings)
        {
            _lastProjPath = TreeViewControl.NormalizeProjKey(settings.LastProjPath);

            Dispatcher.Invoke(() =>
            {
                if (!string.IsNullOrEmpty(_lastProjPath) &&
                    settings.Projects.TryGetValue(_lastProjPath, out var proj) &&
                    !string.IsNullOrEmpty(proj.HmsPath))
                {
                    txtHmsProjectPath.Text = proj.HmsPath;
                    PopulateHmsRunComboBox(proj.HmsPath);
                }

                if (!string.IsNullOrEmpty(settings.DssPath))
                    txtDssFilePath.Text = settings.DssPath;
            });

            // Call async method outside Dispatcher.Invoke so await can resume freely
            if (!string.IsNullOrEmpty(settings.DssPath))
                PopulateDssRunComboBox(settings.DssPath);
        }

        private async void BrowseHmsProject_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select HMS Project File",
                Filter = "HMS Run Files (*.run)|*.run|All Files (*.*)|*.*"
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
            var dialog = new OpenFileDialog
            {
                Title = "Select DSS File",
                Filter = "DSS Files (*.dss)|*.dss|All Files (*.*)|*.*"
            };
            if (dialog.ShowDialog() != true) return;
            string path = dialog.FileName;
            txtDssFilePath.Text = path;
            PopulateDssRunComboBox(path);
            await SaveDssPath(path);
        }

        private void PopulateHmsRunComboBox(string hmsPath)
        {
            if (string.IsNullOrEmpty(hmsPath)) return;
            string dir = File.Exists(hmsPath)
                ? Path.GetDirectoryName(hmsPath) ?? string.Empty
                : hmsPath;
            if (!Directory.Exists(dir)) return;
            var runs = Directory.GetFiles(dir, "*.run", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .ToList();
            cboxHmsRun.ItemsSource = runs;
            if (runs.Count > 0) cboxHmsRun.SelectedIndex = 0;
        }

        private async void PopulateDssRunComboBox(string dssPath)
        {
            if (string.IsNullOrEmpty(dssPath) || !File.Exists(dssPath)) return;
            try
            {
                var allPaths = await Task.Run(() =>
                    HydroExplorer.Utils.DssHyetographReader.GetAllPaths(dssPath));

                var runNames = allPaths
                    .Select(p => {
                        // DSS paths: /A/B/C/D/E/F/ — extract Part F as last non-empty segment
                        var trimmed = p.TrimEnd('/');
                        var lastSlash = trimmed.LastIndexOf('/');
                        if (lastSlash < 0) return string.Empty;
                        var partF = trimmed[(lastSlash + 1)..].Trim();
                        if (partF.StartsWith("RUN:", StringComparison.OrdinalIgnoreCase))
                            partF = partF[4..];
                        return partF;
                    })
                    .Where(f => !string.IsNullOrEmpty(f))
                    .Distinct()
                    .OrderBy(f => f)
                    .ToList();

                System.Diagnostics.Debug.WriteLine($"DSS run names found: {runNames.Count} — {string.Join(", ", runNames)}");

                // Ensure UI update on dispatcher
                Application.Current.Dispatcher.Invoke(() =>
                {
                    cboxDssRun.ItemsSource = runNames;
                    if (runNames.Count > 0)
                    {
                        cboxDssRun.SelectedIndex = 0;
                        EventBus.PublishDssRunSelected(dssPath, runNames[0]);
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PopulateDssRunComboBox error: {ex.Message}");

                if (ex.Message.Contains("version 7", StringComparison.OrdinalIgnoreCase))
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        cboxDssRun.ItemsSource = new List<string>
                            { "⚠ DSS-6 not supported — convert to DSS-7" };
                        cboxDssRun.SelectedIndex = 0;
                    });
                }
            }
        }

        private async Task SaveHmsPath(string path)
        {
            try
            {
                var settings = await _settingsRepo.GetSettingsFresh();
                if (string.IsNullOrEmpty(_lastProjPath))
                    _lastProjPath = TreeViewControl.NormalizeProjKey(settings.LastProjPath);
                if (string.IsNullOrEmpty(_lastProjPath)) return;
                if (!settings.Projects.TryGetValue(_lastProjPath, out var proj))
                    proj = settings.Projects[_lastProjPath] = new ProjectSettings
                    { ProjDir = Path.GetDirectoryName(path) ?? string.Empty };
                proj.HmsPath = path;
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
                settings.DssPath = path;
                await _settingsRepo.SaveSettings(settings);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HydrologyPaneView.SaveDssPath error: {ex.Message}");
            }
        }

        private void TreeViewItem_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
            => e.Handled = true;
    }
}