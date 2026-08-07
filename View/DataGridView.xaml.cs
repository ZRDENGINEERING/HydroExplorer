using HydroExplorer.Helpers;
using HydroExplorer.ViewModel;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;


namespace HydroExplorer.View
{
    public partial class DataGridView : UserControl
    {
        private IUserSettingsRepo? _settingsRepo;
        public Dictionary<string, ProjectSettings> ProjSettings { get; set; } = [];
        public PlotViewModel PlotVm { get; } = App.ServiceProvider.GetRequiredService<PlotViewModel>();
        private readonly SelectionViewModel _selectionVm;
        private readonly HdfFileMonitor _hdfMonitor;

        private List<WSELTableOxy> _allWselData = [];

        public string? projPath = string.Empty;
        public string? hdfPathA = string.Empty;
        public string? hdfPathB = string.Empty;
        public string? planNameA = string.Empty;
        public string? planNameB = string.Empty;
        public string? proName = string.Empty;
        public string filePath = string.Empty;

        private readonly List<HecRasProfileWselResult> itemsSourceA = [];
        private readonly List<HecRasProfileWselResult> itemsSourceB = [];





        public DataGridView()
        {
            InitializeComponent();

            _selectionVm = App.ServiceProvider.GetRequiredService<SelectionViewModel>();
            _selectionVm.PropertyChanged += OnSelectionChanged;

            _hdfMonitor = App.ServiceProvider.GetRequiredService<HdfFileMonitor>();
            _hdfMonitor.FileChanged += OnHdfFileChangedOnDisk;

            PlotVm.PropertyChanged += OnPlotVmPropertyChanged;

            foreach (var reach in PlotVm.Reaches)
                reach.PropertyChanged += OnReachSelectionChanged;

            PlotVm.Reaches.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                    foreach (ReachItem r in e.NewItems)
                        r.PropertyChanged += OnReachSelectionChanged;

                Dispatcher.Invoke(() =>
                {
                    _allWselData = PlotVm.WselData ?? [];
                    //UpdateColumnHeaders();
                    ApplyReachFilter();
                });
            };

            Loaded += (s, e) =>
            {
                if (PlotVm.WselData != null && PlotVm.WselData.Count > 0)
                {
                    _allWselData = PlotVm.WselData;
                    //UpdateColumnHeaders();
                }
                ApplyReachFilter();
                BuildColumnContextMenu();
            };

            IsVisibleChanged += async (s, e) =>
            {
                if (!(bool)e.NewValue) return;

                //System.Diagnostics.Debug.WriteLine($"DataGridView.IsVisibleChanged: WselData count={PlotVm.WselData?.Count ?? 0}");

                await LoadSettingsDataGrid(fresh: true);
                //UpdateColumnHeaders();

                if (PlotVm.WselData != null && PlotVm.WselData.Count > 0)
                {
                    _allWselData = PlotVm.WselData;
                    //UpdateColumnHeaders();
                }

                ApplyReachFilter();
            };

            EventBus.HdfPathChanged += OnHdfPathChanged;
            EventBus.ProfileChanged += OnProfileChanged;

            EventBus.PlanNamesChanged += (nameA, nameB) => Dispatcher.Invoke(() =>
            {
                planNameA = nameA;
                planNameB = nameB;
                //UpdateColumnHeaders();
            });

            EventBus.HdfFileASelected += (_, name) => Dispatcher.Invoke(() =>
            {
                planNameA = name;
                //UpdateColumnHeaders();
            });

            EventBus.HdfFileBSelected += (_, name) => Dispatcher.Invoke(() =>
            {
                planNameB = name;
                //UpdateColumnHeaders();
            });

            Unloaded += (s, e) =>
            {
                PlotVm.PropertyChanged -= OnPlotVmPropertyChanged;
                EventBus.HdfPathChanged -= OnHdfPathChanged;
                EventBus.ProfileChanged -= OnProfileChanged;
                _hdfMonitor.FileChanged -= OnHdfFileChangedOnDisk;
                // Intentionally NOT calling _hdfMonitor.Unwatch/UnwatchAll here —
                // HdfFileMonitor is a shared singleton and other views (e.g.
                // MapOverView) may still depend on watches for these same paths.
            };
        }

        /// <summary>
        /// Fires when a watched HDF file changes on disk (e.g. HEC-RAS re-ran
        /// the plan while the model is open). Only reloads the grid if the
        /// changed file is one of the plans currently displayed.
        /// </summary>
        private void OnHdfFileChangedOnDisk(string path)
        {
            bool matchesA = !string.IsNullOrEmpty(hdfPathA) && PathsEqual(path, hdfPathA);
            bool matchesB = !string.IsNullOrEmpty(hdfPathB) && PathsEqual(path, hdfPathB);
            if (!matchesA && !matchesB) return;

            Dispatcher.InvokeAsync(async () =>
            {
                System.Diagnostics.Debug.WriteLine($"DataGridView: HDF changed on disk — reloading '{Path.GetFileName(path)}'");
                await LoadDataGrid(fresh: true);

                // LoadDataGrid only refreshes settings/paths/headers — the actual
                // WSEL rows come from PlotVm.WselData, which is cached behind a
                // one-time _dataLoaded guard. Force a fresh HDF read so the new
                // data actually flows through OnPlotVmPropertyChanged into the grid.
                await PlotVm.ReloadDataAsync();
            });
        }

        private static bool PathsEqual(string a, string b) =>
            string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

        private void OnPlotVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(PlotViewModel.WselData)) return;

            // Don't filter yet — Reaches hasn't been rebuilt for the new project.
            // CollectionChanged on Reaches will trigger ApplyReachFilter once ready.
            Dispatcher.Invoke(() =>
            {
                _allWselData = PlotVm.WselData ?? [];
                //UpdateColumnHeaders();
            });
        }

        private void BuildColumnContextMenu()
        {
            var menu = new ContextMenu();
            foreach (var column in dgSimple.Columns)
            {
                var header = column.Header?.ToString() ?? "(column)";
                var item = new MenuItem
                {
                    Header = header,
                    IsCheckable = true,
                    IsChecked = true,
                    Tag = column
                };
                item.Click += ColumnMenuItem_Click;
                menu.Items.Add(item);
            }
            dgSimple.ContextMenu = menu;
        }

        private void ColumnMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem) return;
            if (menuItem.Tag is not DataGridColumn column) return;
            column.Visibility = menuItem.IsChecked ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// hdfPathA/hdfPathB now arrive directly in the event payload instead of
        /// being re-read from UserSettings. The settings file write is debounced
        /// (~300ms in HydraulicsPaneView.SaveSettings), so re-reading it here on
        /// every selection change could race ahead of the write and pick up the
        /// previous selection — which is what made "None" on Plan B look like it
        /// did nothing.
        /// </summary>
        private async void OnHdfPathChanged(string newHdfPathA, string newHdfPathB)
        {
            hdfPathA = string.IsNullOrEmpty(newHdfPathA) ? null : newHdfPathA;
            hdfPathB = string.IsNullOrEmpty(newHdfPathB) ? null : newHdfPathB;

            await Dispatcher.InvokeAsync(async () => await LoadDataGrid(fresh: true));
            WatchCurrentHdfPaths();
        }

        private async void OnProfileChanged(string profileName)
        {
            await Dispatcher.InvokeAsync(async () => await LoadDataGrid(fresh: true, overrideProName: profileName));
        }

        /// <summary>
        /// Refreshes non-path settings (proName, projPath) from disk, then
        /// re-applies hdfPathA/hdfPathB from the live event payload (set in
        /// OnHdfPathChanged) so a stale disk read can't clobber them.
        /// </summary>
        private async Task LoadDataGrid(bool fresh = false, string? overrideProName = null)
        {
            try
            {
                var liveHdfPathA = hdfPathA;
                var liveHdfPathB = hdfPathB;

                await LoadSettingsDataGrid(fresh);

                hdfPathA = liveHdfPathA;
                hdfPathB = liveHdfPathB;

                if (overrideProName != null) proName = overrideProName;
                //UpdateColumnHeaders();
                WatchCurrentHdfPaths();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadDataGrid error: {ex.Message}");
            }
        }


        private void WatchCurrentHdfPaths()
        {
            if (!string.IsNullOrEmpty(hdfPathA)) _hdfMonitor.Watch(hdfPathA);
            if (!string.IsNullOrEmpty(hdfPathB)) _hdfMonitor.Watch(hdfPathB);
        }

        private void OnSelectionChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(SelectionViewModel.SelectedRiverSta)) return;
            var rs = _selectionVm.SelectedRiverSta;
            if (string.IsNullOrEmpty(rs)) return;

            var item = dgSimple.Items
                .OfType<WSELTableOxy>()
                .FirstOrDefault(x => x.RiverSta?.Trim() == rs.Trim());

            if (item == null) return;
            dgSimple.Dispatcher.Invoke(() =>
            {
                dgSimple.SelectedItem = item;
                dgSimple.ScrollIntoView(item);
            });
        }

        /// <summary>
        /// Loads proName/projPath (and hdfPathA/hdfPathB as a fallback for
        /// callers that haven't received a live HdfPathChanged payload yet,
        /// e.g. IsVisibleChanged on first show). LoadDataGrid overwrites
        /// hdfPathA/hdfPathB with live values after calling this, so this
        /// method's hdfPathA/hdfPathB assignment only matters on cold start.
        /// </summary>
        private async Task LoadSettingsDataGrid(bool fresh = false)
        {
            _settingsRepo = App.ServiceProvider.GetRequiredService<IUserSettingsRepo>();
            var settings = fresh
                ? await _settingsRepo.GetSettingsFresh()
                : await _settingsRepo.GetSettings();

            projPath = settings.ProjPath;

            if (string.IsNullOrEmpty(projPath)) return;
            if (!settings.Projects.TryGetValue(projPath, out var project)) return;

            hdfPathA = project.HdfPathA;
            hdfPathB = project.HdfPathB;
            planNameA = project.PlanNameA;
            planNameB = project.PlanNameB;
            proName = project.ProName;
        }

        public static HecRasProfileWselResult GetStaInfo(List<HecRasProfileWselResult> calcComp)
            => calcComp.ElementAt(11);

        public List<HecRasProfileWselResult> CalcCompare()
        {
            int resN1 = itemsSourceA.Count;
            int resN2 = itemsSourceB.Count;
            int resN = Math.Min(resN1, resN2);
            var results = new List<HecRasProfileWselResult>(resN);

            if (resN1 != resN2)
            {
                var arr1 = itemsSourceA.Take(resN).Select(x => x.RiverSta).ToArray();
                var arr2 = itemsSourceB.Take(resN).Select(x => x.RiverSta).ToArray();

                for (int i = 0; i < resN; i++)
                {
                    string rsta = resN1 > resN2 ? arr2[i] : arr1[i];
                    int idx1 = Array.IndexOf(arr1, rsta);
                    int idx2 = Array.IndexOf(arr2, rsta);
                    double delta = (idx1 >= 0 && idx2 >= 0)
                        ? itemsSourceB[idx2].WSElev - itemsSourceA[idx1].WSElev
                        : 0;
                    results.Add(new HecRasProfileWselResult
                    {
                        RiverSta = rsta,
                        WSElev = Math.Round(delta, 2),
                        DELTA = Math.Round(delta, 2)
                    });
                }
            }
            else
            {
                for (int i = 0; i < resN; i++)
                    results.Add(new HecRasProfileWselResult
                    {
                        RiverSta = itemsSourceA[i].RiverSta,
                        WSElev = Math.Round(itemsSourceB[i].WSElev - itemsSourceA[i].WSElev, 2),
                    });
            }
            return results;
        }

        private void Click_Me(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            MessageBox.Show(btn.Content.ToString() + " button clicked");
        }

        private void DgSimpleSlctChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgSimple.SelectedItem is WSELTableOxy row)
            {
                _selectionVm.SelectedRiverSta = row.RiverSta;
                _selectionVm.SelectedDelta = row.DELTA;
            }
        }

        private void UpdateColumnHeaders()
        {
            var nameA = string.IsNullOrEmpty(planNameA) ? "HDF A" : planNameA;
            var nameB = string.IsNullOrEmpty(planNameB) ? "HDF B" : planNameB;

            foreach (var col in dgSimple.Columns)
            {
                var h = col.Header?.ToString() ?? "";
                if (h == "QTotal A" || (h.StartsWith("QTotal (") && h.Contains(nameA)))
                    col.Header = $"QTotal ({nameA})";
                else if (h == "WSElev A" || (h.StartsWith("WSElev (") && h.Contains(nameA)))
                    col.Header = $"WSElev ({nameA})";
                else if (h == "QTotal B" || (h.StartsWith("QTotal (") && h.Contains(nameB)))
                    col.Header = $"QTotal ({nameB})";
                else if (h == "WSElev B" || (h.StartsWith("WSElev (") && h.Contains(nameB)))
                    col.Header = $"WSElev ({nameB})";
            }
        }


        private void OnReachSelectionChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ReachItem.IsSelected)) return;
            Dispatcher.Invoke(ApplyReachFilter);
        }

        private void ApplyReachFilter()
        {
            var selectedReaches = PlotVm.Reaches
                .Where(r => r.IsSelected)
                .Select(r => r.ReachId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            //System.Diagnostics.Debug.WriteLine(
            //    $"ApplyReachFilter: _allWselData={_allWselData.Count} selectedReaches=[{string.Join(",", selectedReaches)}] PlotVm.Reaches={PlotVm.Reaches.Count}");

            dgSimple.ItemsSource = selectedReaches.Count == 0
                ? _allWselData
                : _allWselData
                    .Where(r => !string.IsNullOrWhiteSpace(r.Reach) &&
                                selectedReaches.Contains(r.Reach))
                    .ToList();

            //System.Diagnostics.Debug.WriteLine($"ApplyReachFilter: dgSimple.ItemsSource count={((System.Collections.IList?)dgSimple.ItemsSource)?.Count ?? 0}");
        }
    }

    public class HecRasProfileResult : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public string River { get; set; }
        public string Reach { get; set; }
        public string RiverSta { get; set; }
        public string Profile { get; set; }
        public float QTotal { get; set; }
        public float MinChEl { get; set; }
        public double WSElev { get; set; }
        public double MaxDepths { get; set; }
        public double CritWS { get; set; }
        public double EGElev { get; set; }
        public double EGSlope { get; set; }
        public double VelChnl { get; set; }
        public double FlowArea { get; set; }
        public double TopWidth { get; set; }
        public double FrChnl { get; set; }
    }

    public class HecRasProfileWselResult : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public string RiverSta { get; set; }
        public double WSElev { get; set; }
        public double DELTA { get; set; }
    }

    public class WSELTable : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public string River { get; set; }
        public string Reach { get; set; }
        public string RiverSta { get; set; }
        public string Profile { get; set; }
        public float QTotalA { get; set; }
        public double WSElevA { get; set; }
        public float QTotalB { get; set; }
        public double WSElevB { get; set; }
        public double DELTA { get; set; }
    }

    public class WSELTableOxy : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public string River { get; set; }
        public string Reach { get; set; }
        public string RiverSta { get; set; }
        public string Profile { get; set; }
        public float QTotalA { get; set; }
        public float MinChEl { get; set; }
        public double WSElevA { get; set; }
        public float QTotalB { get; set; }
        public double WSElevB { get; set; }
        public double DELTA { get; set; }

        public string QTotalADisplay => float.IsNaN(QTotalA) ? "—" : QTotalA.ToString("F0");
        public string WSElevADisplay => double.IsNaN(WSElevA) ? "—" : WSElevA.ToString("F2");
        public string QTotalBDisplay => float.IsNaN(QTotalB) ? "—" : QTotalB.ToString("F0");
        public string WSElevBDisplay => double.IsNaN(WSElevB) ? "—" : WSElevB.ToString("F2");
        public string DELTADisplay => double.IsNaN(DELTA) ? "—" : DELTA.ToString("F2");
    }
}