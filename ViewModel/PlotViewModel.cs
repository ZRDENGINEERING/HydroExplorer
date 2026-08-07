using BruTile;
using HydroExplorer.Core;
using HydroExplorer.Helpers;
using HydroExplorer.Themes;
using HydroExplorer.Utils;
using HydroExplorer.View;
using Microsoft.Extensions.DependencyInjection;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;



namespace HydroExplorer.ViewModel
{
    // ── Reach checkbox item ──────────────────────────────────────────────────
    public class ReachItem : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public string ReachId { get; set; }

        private bool _isSelected = false;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ── Main ViewModel ───────────────────────────────────────────────────────
    public class PlotViewModel : INotifyPropertyChanged
    {
        private bool _dataLoaded = false;

        private readonly HecRasHdfReader _reader = new();
        private IUserSettingsRepo? _settingsRepo;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private string _projPath = string.Empty;

        private CancellationTokenSource? _loadCts;

        private enum PlotMode { Both, BOnly, AOnly }

        private PlotMode _plotMode = PlotMode.Both;


        // ── Plot model ───────────────────────────────────────────────────────
        private PlotModel _plotModel;
        public PlotModel PlotModel
        {
            get => _plotModel;
            set
            {
                if (_plotModel != null)
                {
                    _plotModel.InvalidatePlot(false);
                }
                _plotModel = value;
                OnPropertyChanged();
            }
        }

        private PlotModel _plotModelChartsTab;
        public PlotModel PlotModelChartsTab
        {
            get => _plotModelChartsTab;
            set
            {
                if (_plotModelChartsTab != null)
                {
                    _plotModelChartsTab.InvalidatePlot(false);
                }
                _plotModelChartsTab = value;
                OnPropertyChanged();
            }
        }

        private PlotModel _plotModelB = new();
        public PlotModel PlotModelB
        {
            get => _plotModelB;
            set
            {
                if (_plotModelB != null)
                    _plotModelB.InvalidatePlot(false);
                _plotModelB = value;
                OnPropertyChanged();
            }
        }

        private PlotModel vm1;
        public PlotModel Vm { get => vm1; set => SetProperty(ref vm1, value); }

        // ── Reach data ───────────────────────────────────────────────────────
        private List<WSELTableOxy>? _wselData = [];
        public List<WSELTableOxy>? WselData
        {
            get => _wselData;
            private set { _wselData = value; OnPropertyChanged(); }
        }



        private Dictionary<string, List<WSELTableOxy>> _dataByReach = [];

        public ObservableCollection<ReachItem> Reaches { get; set; } = [];

        //public bool HasMultipleReaches => true; // force visible for debugging
        public bool HasMultipleReaches => Reaches.Count > 1;

        public ICommand ReachSelectionChangedCommand { get; }





        public PlotViewModel()
        {
            PlotModel = new PlotModel();
            PlotModelChartsTab = new PlotModel();
            PlotModelB = new PlotModel();
            Vm = new PlotModel();

            ReachSelectionChangedCommand = new RelayCommand(execute: _ => RefreshPlot(), canExecute: _ => true);

            EventBus.ProjPathChanged += async path =>
            {
                _loadCts?.Cancel();

                var oldCts = _loadCts;
                _loadCts = new CancellationTokenSource();
                var token = _loadCts.Token;

                oldCts?.Dispose();

                _dataLoaded = false;

                try
                {
                    await LoadDataAsync(path, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    System.Diagnostics.Debug.WriteLine("PlotViewModel Base LoadDataAsync cancelled.");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"PlotViewModel ProjPathChanged error: {ex.Message}");
                }
            };

            // Plan A/B combobox changes. Previously nothing subscribed to this
            // at all, so WselData (what DataGridView actually renders, via
            // ApplyReachFilter) only ever refreshed on a project change — Plan
            // A/B selections never reached this ViewModel. hdfPathA/hdfPathB
            // come straight from the event payload, not settings, so this
            // can't race the debounced settings write either.
            EventBus.HdfPathChanged += async (hdfPathA, hdfPathB) =>
            {
                try
                {
                    await ReloadDataAsync(hdfPathA, hdfPathB);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"PlotViewModel HdfPathChanged error: {ex.Message}");
                }
            };

            // Profile combobox changes — same gap as above; nothing reloaded
            // WselData when only the profile changed.
            EventBus.ProfileChanged += async profile =>
            {
                try
                {
                    await ReloadDataAsync(overrideProName: profile);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"PlotViewModel ProfileChanged error: {ex.Message}");
                }
            };
        }





        private readonly SemaphoreSlim _reloadLock = new(1, 1);

        /// <summary>
        /// Forces a fresh HDF re-read even if data was already loaded — used
        /// when the underlying HDF file changes on disk (e.g. HdfFileMonitor
        /// detects HEC-RAS re-ran the plan), when Plan A/B changes via
        /// HdfPathChanged, or when the profile changes via ProfileChanged.
        /// The project path itself hasn't necessarily changed in any of these
        /// cases, so EventBus.ProjPathChanged won't fire on its own.
        ///
        /// hdfPathAOverride/hdfPathBOverride/overrideProName, when supplied
        /// (non-null — "" is a valid "no Plan B" value, not "unset"), come
        /// from a live event payload and take precedence over whatever is
        /// currently in settings, avoiding a race against the debounced
        /// settings write. Left null for the file-changed-on-disk and
        /// project-change call sites, which have no live override to give and
        /// fall back to settings.
        ///
        /// Serialized via _reloadLock — hdfPathA and hdfPathB are watched by
        /// separate FileSystemWatchers with independent debounce timers, so a
        /// batch HEC-RAS run can fire two FileChanged events seconds apart.
        /// Without this lock, two overlapping calls can race on constructing
        /// and assigning PlotModel, which throws "This PlotModel is already
        /// in use by some other PlotView control." A second call arriving
        /// while the first is still running simply waits, then runs once the
        /// first is done — cheap enough for a 3-second-debounced event.
        /// </summary>
        public async Task ReloadDataAsync(string? hdfPathAOverride = null, string? hdfPathBOverride = null, string? overrideProName = null)
        {
            await _reloadLock.WaitAsync();
            try
            {
                _loadCts?.Cancel();

                var oldCts = _loadCts;
                _loadCts = new CancellationTokenSource();
                var token = _loadCts.Token;

                oldCts?.Dispose();

                _dataLoaded = false;

                try
                {
                    await LoadDataAsync(
                        token: token,
                        overrideHdfPathA: hdfPathAOverride,
                        overrideHdfPathB: hdfPathBOverride,
                        overrideProName: overrideProName).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    System.Diagnostics.Debug.WriteLine("PlotViewModel ReloadDataAsync cancelled.");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"PlotViewModel ReloadDataAsync error: {ex.Message}");
                }
            }
            finally
            {
                _reloadLock.Release();
            }
        }

        public async Task LoadDataAsync(
            string projPathOverride = "",
            CancellationToken token = default,
            string? overrideHdfPathA = null,
            string? overrideHdfPathB = null,
            string? overrideProName = null)
        {
            if (_dataLoaded) return;
            _dataLoaded = true;

            try
            {
                token.ThrowIfCancellationRequested();

                _settingsRepo = App.ServiceProvider.GetRequiredService<IUserSettingsRepo>();

                var settings = string.IsNullOrEmpty(projPathOverride)
                    ? await _settingsRepo.GetSettings()
                    : await _settingsRepo.GetSettingsFresh();

                token.ThrowIfCancellationRequested();

                _projPath = string.IsNullOrEmpty(projPathOverride)
                    ? settings.ProjPath
                    : projPathOverride;

                if (string.IsNullOrEmpty(_projPath))
                {
                    System.Diagnostics.Debug.WriteLine("LoadDataAsync: No project path.");
                    _dataLoaded = false;
                    return;
                }

                if (!settings.Projects.TryGetValue(_projPath, out var project))
                {
                    System.Diagnostics.Debug.WriteLine($"LoadDataAsync: No saved settings for '{_projPath}'.");
                    WselData = [];
                    Application.Current.Dispatcher.Invoke(() => LoadFromWSELTable(null));
                    _dataLoaded = false;
                    return;
                }

                var hdfPathA = overrideHdfPathA ?? project.HdfPathA;
                var hdfPathB = overrideHdfPathB ?? project.HdfPathB;
                var proName = overrideProName ?? project.ProName;

                token.ThrowIfCancellationRequested();

                bool hasA = !string.IsNullOrEmpty(hdfPathA) && File.Exists(hdfPathA);
                bool hasB = !string.IsNullOrEmpty(hdfPathB) && File.Exists(hdfPathB);

                if (!hasA && !hasB)
                {
                    WselData = [];
                    Application.Current.Dispatcher.Invoke(() => LoadFromWSELTable(null));
                    _dataLoaded = false;
                    return;
                }

                _plotMode = (hasA, hasB) switch
                {
                    (true, true) => PlotMode.Both,
                    (false, true) => PlotMode.BOnly,
                    (true, false) => PlotMode.AOnly,
                    _ => PlotMode.Both
                };

                string? effectiveA = hasA ? hdfPathA : null;
                string? effectiveB = hasB ? hdfPathB : null;

                if (!hasA && hasB)
                {
                    effectiveA = hdfPathB;
                    effectiveB = null;
                }
                else if (!hasB)
                {
                    effectiveB = null;
                }

                token.ThrowIfCancellationRequested();

                string? profileWarning = null;
                WselData = await Task.Run(() =>
                {
                    if (_plotMode != PlotMode.Both)
                    {
                        string singlePath = hasA ? hdfPathA : hdfPathB;
                        var single = HecRasHdfReader.ReadWSELTableOxySingle(singlePath, proName);
                        return single ?? [];
                    }

                    var result = HecRasHdfReader.ReadWSELTableOxy(effectiveA, effectiveB, proName, out profileWarning);
                    return result;
                }, token);

                EventBus.PublishProfileMismatchWarning(profileWarning ?? string.Empty);

                token.ThrowIfCancellationRequested();

                if (WselData == null) return;

                var data = WselData;
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    LoadFromWSELTable(data, project.SelectedReaches);
                });
            }
            catch (OperationCanceledException)
            {
                _dataLoaded = false;
                System.Diagnostics.Debug.WriteLine("LoadDataAsync cancelled.");
            }
            catch (Exception ex)
            {
                _dataLoaded = false;
                System.Diagnostics.Debug.WriteLine($"LoadDataAsync error: {ex.Message}");
            }
        }









        public void LoadFromWSELTable(List<WSELTableOxy>? data, List<string>? savedReaches = null)
        {
            if (data == null)
            {
                _dataByReach = [];
                Reaches.Clear();
                OnPropertyChanged(nameof(HasMultipleReaches));
                RefreshPlot();
                return;
            }

            _dataByReach = data
                .GroupBy(r => string.IsNullOrWhiteSpace(r.Reach) ? "Default" : r.Reach)
                .ToDictionary(g => g.Key, g => g.ToList());

            Reaches.Clear();
            bool isFirst = true;
            foreach (var key in _dataByReach.Keys)
            {
                bool isSelected = savedReaches != null && savedReaches.Count > 0
                    ? savedReaches.Contains(key, StringComparer.OrdinalIgnoreCase)
                    : isFirst;  // default: first reach selected

                Reaches.Add(new ReachItem { Name = key, ReachId = key, IsSelected = isSelected });
                isFirst = false;
            }

            OnPropertyChanged(nameof(HasMultipleReaches));
            RefreshPlot();
        }










        private async void RefreshPlot()
        {
            try
            {
                var selectedData = Reaches
                    .Where(r => r.IsSelected)
                    .SelectMany(r => _dataByReach.TryGetValue(r.ReachId, out var rows)
                        ? rows
                        : Enumerable.Empty<WSELTableOxy>())
                    .ToList();

                // Mutate the existing PlotModel instances in place rather than
                // replacing the object reference. Reassigning PlotModel to a new
                // instance forces every bound PlotView to detach/reattach, which
                // is exactly the operation that throws "This PlotModel is
                // already in use by some other PlotView control." if a second
                // PlotView (anywhere, however it's wired) is still transitioning
                // off the old instance. Mutating in place means no PlotView ever
                // needs to re-attach, so that whole failure mode is impossible.
                PopulatePlot(PlotModel, selectedData, _plotMode);
                PlotModel.InvalidatePlot(true);

                PopulatePlot(PlotModelChartsTab, selectedData, _plotMode);
                PlotModelChartsTab.InvalidatePlot(true);

                await SaveSelectedReachAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RefreshPlot error: {ex.Message}");
            }
        }




        private async Task SaveSelectedReachAsync()
        {
            try
            {
                if (_settingsRepo == null || string.IsNullOrEmpty(_projPath)) return;

                var selectedReaches = Reaches
                    .Where(r => r.IsSelected)
                    .Select(r => r.ReachId)
                    .ToList();

                var settings = await _settingsRepo.GetSettings();
                if (settings.Projects.TryGetValue(_projPath, out var project))
                {
                    project.SelectedReaches = selectedReaches;
                    await _settingsRepo.SaveSettings(settings);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SaveSelectedReachAsync error: {ex.Message}");
            }
        }




        private static void PopulatePlot(PlotModel model, List<WSELTableOxy> data, PlotMode mode = PlotMode.Both)
        {
            // Clear and rebuild in place — never replace the PlotModel object
            // itself. See RefreshPlot for why.
            model.Series.Clear();
            model.Axes.Clear();
            model.Annotations.Clear();
            model.Legends.Clear();

            model.TitlePadding = 0;
            model.TitleFontSize = 12;
            model.DefaultFontSize = 10;
            model.SubtitleFontSize = 10;
            model.TextColor = OxyColorPalette.Colors["TextAxis"];
            model.PlotMargins = new OxyThickness(38, 5, 5, 40);
            model.PlotAreaBorderColor = OxyColorPalette.Colors["DimGray"];
            model.PlotAreaBorderThickness = new OxyThickness(1);

            static LinearAxis MakeAxis(AxisPosition pos, bool reversed = false) => new()
            {
                Position = pos,
                StartPosition = reversed ? 1 : 0,
                EndPosition = reversed ? 0 : 1,
                AxislineStyle = LineStyle.Solid,
                AxislineColor = OxyColorPalette.Colors["DimGray"],
                AxislineThickness = 1,
                TickStyle = TickStyle.Outside,
                TicklineColor = OxyColorPalette.Colors["DimGray"],
                MajorTickSize = 5,
                MinorTickSize = 3,
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColorPalette.Colors["DimGray"],
                MinorGridlineStyle = LineStyle.Dot,
                MinorGridlineColor = OxyColorPalette.Colors["DimGray"],
                TextColor = OxyColorPalette.Colors["TextAxis"],
                FontSize = 10,
            };

            var xAxis = MakeAxis(AxisPosition.Bottom, reversed: true);
            xAxis.MinimumMajorStep = 5000;
            xAxis.MinimumMinorStep = 1000;
            //xAxis.MajorStep = 1000;
            //xAxis.MinorStep = 500;

            model.Axes.Add(xAxis);
            model.Axes.Add(MakeAxis(AxisPosition.Left));

            var seriesMinChEl = new LineSeries
            {
                Title = "MinChEl",
                Color = OxyColors.Red,
                StrokeThickness = 1,
            };

            var areaMinChEl = new AreaSeries
            {
                Title = null,
                Color = OxyColors.Transparent,
                Fill = OxyColor.FromAColor(35, OxyColors.SaddleBrown),
            };

            var seriesA = new LineSeries
            {
                Title = mode == PlotMode.Both ? "Plan A" : "Plan",
                Color = OxyColors.Green,
                StrokeThickness = 2,
                MarkerSize = 4
            };

            var seriesB = new LineSeries
            {
                Title = mode == PlotMode.Both ? "Plan B" : "Plan",
                Color = OxyColors.Blue,
                StrokeThickness = 2,
                MarkerSize = 4
            };

            var deltaMarkers = new ScatterSeries
            {
                Title = "Delta > 0",
                MarkerType = MarkerType.Triangle,
                MarkerSize = 6,
                MarkerFill = OxyColorPalette.Colors["Crimson"]
            };

            double minChElFloor = double.MaxValue;

            foreach (var row in data)
            {
                var cleaned = row.RiverSta?.Replace(",", "").Replace("*", "").Trim();

                if (!double.TryParse(cleaned,
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double sta))
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Failed to parse: '{row.RiverSta}'");
                    continue;
                }

                seriesMinChEl.Points.Add(new DataPoint(sta, row.MinChEl));
                areaMinChEl.Points.Add(new DataPoint(sta, row.MinChEl));
                if (row.MinChEl < minChElFloor) minChElFloor = row.MinChEl;

                // Only add the series that have data
                if (mode != PlotMode.BOnly)
                    seriesA.Points.Add(new DataPoint(sta, row.WSElevA));

                seriesB.Points.Add(new DataPoint(sta, row.WSElevB));

                // Only show deltas when comparing two plans
                if (mode == PlotMode.Both && row.DELTA > 0)
                {
                    deltaMarkers.Points.Add(new ScatterPoint(sta, row.WSElevB));

                    model.Annotations.Add(new LineAnnotation
                    {
                        Type = LineAnnotationType.Vertical,
                        X = sta,
                        Color = OxyColorPalette.Colors["Tomato"],
                        StrokeThickness = 1.5,
                        LineStyle = LineStyle.Dash
                    });

                    model.Annotations.Add(new TextAnnotation
                    {
                        Text = row.RiverSta,
                        TextPosition = new DataPoint(sta, row.WSElevB),
                        TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Right,
                        TextVerticalAlignment = OxyPlot.VerticalAlignment.Bottom,
                        FontSize = 9,
                        TextColor = OxyColorPalette.Colors["TextLight"],
                        StrokeThickness = 0,
                        TextRotation = -90
                    });
                }
            }

            areaMinChEl.ConstantY2 = minChElFloor == double.MaxValue ? 0 : minChElFloor - 5;

            model.Series.Add(areaMinChEl);
            model.Series.Add(seriesMinChEl);

            if (mode != PlotMode.BOnly)
                model.Series.Add(seriesA);

            model.Series.Add(seriesB);

            if (mode == PlotMode.Both)
                model.Series.Add(deltaMarkers);

            model.Legends.Add(new Legend
            {
                LegendPosition = LegendPosition.TopRight,
                LegendPlacement = LegendPlacement.Inside,
                LegendFontSize = 10
            });
        }



        protected bool SetProperty<T>(ref T field, T newValue, [CallerMemberName] string? propertyName = null)
        {
            if (!Equals(field, newValue))
            {
                field = newValue;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
                return true;
            }
            return false;
        }




    }
}