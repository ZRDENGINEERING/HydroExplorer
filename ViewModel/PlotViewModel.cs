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
using System.Windows.Input;
using System.Windows;



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
                    System.Diagnostics.Debug.WriteLine("PlotViewModel LoadDataAsync cancelled.");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"PlotViewModel ProjPathChanged error: {ex.Message}");
                }
            };
        }





        private readonly SemaphoreSlim _reloadLock = new(1, 1);

        /// <summary>
        /// Forces a fresh HDF re-read even if data was already loaded — used
        /// when the underlying HDF file changes on disk (e.g. HdfFileMonitor
        /// detects HEC-RAS re-ran the plan) and the project path itself hasn't
        /// changed, so EventBus.ProjPathChanged won't fire on its own.
        /// Reuses the same cancellation pattern as the ProjPathChanged handler.
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
        public async Task ReloadDataAsync()
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
                    await LoadDataAsync(token: token).ConfigureAwait(false);
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

        public async Task LoadDataAsync(string projPathOverride = "", CancellationToken token = default)
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

                var hdfPathA = project.HdfPathA;
                var hdfPathB = project.HdfPathB;
                var proName = project.ProName;

                //if (string.IsNullOrEmpty(hdfPathA) || !File.Exists(hdfPathA))
                //{
                //    System.Diagnostics.Debug.WriteLine($"LoadDataAsync: HDF Path A missing or not found: '{hdfPathA}'.");
                //    _dataLoaded = false; // reset so retry is possible
                //    return;
                //}

                //if (string.IsNullOrEmpty(hdfPathB) || !File.Exists(hdfPathB))
                //{
                //    System.Diagnostics.Debug.WriteLine($"LoadDataAsync: HDF Path B missing or not found: '{hdfPathB}'.");
                //    _dataLoaded = false;
                //    return;
                //}

                token.ThrowIfCancellationRequested();

                bool hasA = !string.IsNullOrEmpty(hdfPathA) && File.Exists(hdfPathA);
                bool hasB = !string.IsNullOrEmpty(hdfPathB) && File.Exists(hdfPathB);

                if (!hasA && !hasB)
                {
                    //System.Diagnostics.Debug.WriteLine("LoadDataAsync: No valid HDF files found. Leaving plot blank.");
                    WselData = [];
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

                // If only one exists, use it as B (primary) and leave A null
                if (!hasA && hasB)
                {
                    effectiveA = null;
                    effectiveB = hdfPathB;
                }
                else if (hasA && !hasB)
                {
                    effectiveA = null;
                    effectiveB = hdfPathA;   // promote A to B slot so reader always gets a primary
                }

                token.ThrowIfCancellationRequested();

                string? profileWarning = null;
                WselData = await Task.Run(() =>
                {
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
                Reaches.Clear();
                OnPropertyChanged(nameof(HasMultipleReaches));
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
            xAxis.MajorStep = 1000;
            xAxis.MinorStep = 500;
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
                Fill = OxyColor.FromAColor(50, OxyColors.SaddleBrown),
            };

            var seriesA = new LineSeries
            {
                Title = mode == PlotMode.Both ? "Plan A" : "Plan",
                Color = OxyColors.GreenYellow,
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