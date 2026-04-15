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
        public List<WSELTableOxy>? WselData { get; private set; } = [];

        private Dictionary<string, List<WSELTableOxy>> _dataByReach = [];

        public ObservableCollection<ReachItem> Reaches { get; set; } = [];

        //public bool HasMultipleReaches => true; // force visible for debugging
        public bool HasMultipleReaches => Reaches.Count > 1;

        public ICommand ReachSelectionChangedCommand { get; }



        public PlotViewModel()
        {
            PlotModel = new PlotModel();
            ReachSelectionChangedCommand = new RelayCommand(execute: _ => RefreshPlot(), canExecute: _ => true);

            EventBus.ProjPathChanged += async path =>
            {
                _loadCts?.Cancel();

                var oldCts = _loadCts;
                _loadCts = new CancellationTokenSource();
                var token = _loadCts.Token;

                oldCts?.Dispose();

                try
                {
                    _dataLoaded = false;

                    // Delay without throwing on cancellation
                    await Task.Delay(1000).ContinueWith(_ => { }, token);

                    if (token.IsCancellationRequested) return;

                    await LoadDataAsync(path);
                }
                catch (TaskCanceledException)
                {
                    System.Diagnostics.Debug.WriteLine("PlotViewModel load delay cancelled.");
                }
                catch (OperationCanceledException)
                {
                    System.Diagnostics.Debug.WriteLine("PlotViewModel ProjPathChanged cancelled.");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"PlotViewModel ProjPathChanged error: {ex.Message}");
                }
            };
        }





        public async Task LoadDataAsync(string projPathOverride = "")
        {
            if (_dataLoaded) return;
            _dataLoaded = true;

            try
            {
                _settingsRepo = App.ServiceProvider.GetRequiredService<IUserSettingsRepo>();

                var settings = string.IsNullOrEmpty(projPathOverride)
                    ? await _settingsRepo.GetSettings()
                    : await _settingsRepo.GetSettingsFresh();

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
                    _dataLoaded = false;
                    return;
                }




                var hdfPathA = project.HdfPathA;
                var hdfPathB = project.HdfPathB;
                var proName = project.ProName;

                if (string.IsNullOrEmpty(hdfPathA) || !File.Exists(hdfPathA))
                {
                    System.Diagnostics.Debug.WriteLine($"LoadDataAsync: HDF Path A missing or not found: '{hdfPathA}'.");
                    return;
                }

                if (string.IsNullOrEmpty(hdfPathB) || !File.Exists(hdfPathB))
                {
                    System.Diagnostics.Debug.WriteLine($"LoadDataAsync: HDF Path B missing or not found: '{hdfPathB}'.");
                    return;
                }

                WselData = await Task.Run(() =>
                    HecRasHdfReader.ReadWSELTableOxy(hdfPathA, hdfPathB, proName));

                if (WselData == null) return;

                var data = WselData;
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    LoadFromWSELTable(data, project.SelectedReach);
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









        public void LoadFromWSELTable(List<WSELTableOxy>? data, string savedReach = "")
        {
            if (data == null) return;

            _dataByReach = data
                .GroupBy(r => string.IsNullOrWhiteSpace(r.Reach) ? "Default" : r.Reach)
                .ToDictionary(g => g.Key, g => g.ToList());

            Reaches.Clear();
            bool isFirst = true;
            foreach (var key in _dataByReach.Keys)
            {
                bool isSelected = string.IsNullOrWhiteSpace(savedReach)
                    ? isFirst
                    : key == savedReach;

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

                PlotModel = CreatePlot(selectedData);
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

                var selectedReach = Reaches.FirstOrDefault(r => r.IsSelected)?.ReachId ?? string.Empty;

                var settings = await _settingsRepo.GetSettings();
                if (settings.Projects.TryGetValue(_projPath, out var project))
                {
                    project.SelectedReach = selectedReach;
                    await _settingsRepo.SaveSettings(settings);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SaveSelectedReachAsync error: {ex.Message}");
            }
        }




        private static PlotModel CreatePlot(List<WSELTableOxy> data)
        {
            var model = new PlotModel
            {
                TitlePadding = 0,
                TitleFontSize = 12,
                DefaultFontSize = 10,
                SubtitleFontSize = 10,
                TextColor = OxyColorPalette.Colors["TextAxis"],
                PlotMargins = new OxyThickness(30, 10, 10, 30),
                PlotAreaBorderColor = OxyColorPalette.Colors["DimGray"],
                PlotAreaBorderThickness = new OxyThickness(1),
            };

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

            var seriesA = new LineSeries
            {
                Title = "Plan A",
                Color = OxyColors.GreenYellow,
                StrokeThickness = 2,
                MarkerSize = 4
            };

            var seriesB = new LineSeries
            {
                Title = "Plan B",
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

            foreach (var row in data)
            {
                var cleaned = row.RiverSta?.Replace(",", "").Trim();
                if (!double.TryParse(cleaned,
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double sta))
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Failed to parse: '{row.RiverSta}'");
                    continue;
                }

                seriesMinChEl.Points.Add(new DataPoint(sta, row.MinChEl));
                seriesA.Points.Add(new DataPoint(sta, row.WSElevA));
                seriesB.Points.Add(new DataPoint(sta, row.WSElevB));

                if (row.DELTA > 0)
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

            model.Series.Add(seriesMinChEl);
            model.Series.Add(seriesA);
            model.Series.Add(seriesB);
            model.Series.Add(deltaMarkers);

            model.Legends.Add(new Legend
            {
                LegendPosition = LegendPosition.TopRight,
                LegendPlacement = LegendPlacement.Inside,
                LegendFontSize = 10
            });

            return model;
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