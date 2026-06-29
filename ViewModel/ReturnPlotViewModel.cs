using HydroExplorer.Core;
using HydroExplorer.Helpers;
using HydroExplorer.Themes;
using HydroExplorer.Utils;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;

namespace HydroExplorer.ViewModel
{
    public class ReturnPlotViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

        private PlotModel? _plotModel;
        public PlotModel? PlotModel
        {
            get => _plotModel;
            set { _plotModel?.InvalidatePlot(false); _plotModel = value; OnPropertyChanged(); }
        }

        // ── Run picker ──────────────────────────────────────────────────────
        public ObservableCollection<string> AvailableRunNames { get; } = [];

        private string? _selectedRunToAdd;
        public string? SelectedRunToAdd
        {
            get => _selectedRunToAdd;
            set { _selectedRunToAdd = value; OnPropertyChanged(); }
        }

        public ICommand AddRunCommand { get; }
        public ICommand ClearAddedRunsCommand { get; }

        private string _dssFile = string.Empty;

        // Auto-matched runs (by return-period suffix) — rebuilt on every LoadAsync.
        private List<(double ReturnPeriod, string RunName, List<DssHydrographReader.HydrographRecord> Records)> _autoRuns = [];

        // User-added runs, layered on top — persists across re-renders until
        // the DSS file itself changes (run names wouldn't carry over anyway).
        private readonly List<(string RunName, List<DssHydrographReader.HydrographRecord> Records)> _manualRuns = [];

        // Run name suffixes to look for — matched against Part F after stripping "RUN:" prefix
        // These are the return period suffixes; the prefix (e.g. "BOG_") varies by project
        private static readonly (string Suffix, double ReturnPeriod)[] ReturnPeriods =
        [
            ("2YR",   2),
            ("5YR",   5),
            ("10YR",  10),
            ("25YR",  25),
            ("50YR",  50),
            ("100YR", 100),
            ("500YR", 500),
        ];

        private static readonly OxyColor[] RunColors =
        [
            OxyColors.SteelBlue,
            OxyColors.MediumSeaGreen,
            OxyColors.Goldenrod,
            OxyColors.DarkOrange,
            OxyColors.Tomato,
            OxyColors.Crimson,
            OxyColors.MediumPurple,
            OxyColors.CadetBlue,
            OxyColors.Chocolate,
            OxyColors.DarkSlateGray,
        ];

        public ReturnPlotViewModel()
        {
            PlotModel = BuildEmptyPlot();

            AddRunCommand = new RelayCommand(async () => await AddSelectedRunAsync());
            ClearAddedRunsCommand = new RelayCommand(async () => await ClearManualRunsAsync());

            // Reload when user changes DSS file or run in Hydrology tab
            EventBus.DssRunSelected += (dssPath, _) => LoadAsync(dssPath);
        }

        private async void LoadAsync(string dssFile)
        {
            if (!File.Exists(dssFile)) return;

            bool isNewFile = !string.Equals(_dssFile, dssFile, StringComparison.OrdinalIgnoreCase);
            _dssFile = dssFile;

            if (isNewFile)
                _manualRuns.Clear();

            try
            {
                var runNames = await GetRunNamesAsync(dssFile);

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    AvailableRunNames.Clear();
                    foreach (var r in runNames.OrderBy(r => r))
                        AvailableRunNames.Add(r);
                });

                // Match return period suffixes against available run names
                _autoRuns = [];

                foreach (var (suffix, rp) in ReturnPeriods)
                {
                    var matchedRun = runNames.FirstOrDefault(r =>
                        r.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

                    if (matchedRun == null) continue;

                    var records = await System.Threading.Tasks.Task.Run(() =>
                        DssHydrographReader.ReadFlow(dssFile, matchedRun));

                    if (records.Count > 0)
                        _autoRuns.Add((rp, matchedRun, records));
                }

                await RebuildPlotAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ReturnPlotViewModel error: {ex.Message}");
            }
        }

        private async Task<List<string>> GetRunNamesAsync(string dssFile)
        {
            var allPaths = await System.Threading.Tasks.Task.Run(() =>
                DssHyetographReader.GetAllPaths(dssFile));

            return allPaths
                .Select(p =>
                {
                    var trimmed = p.TrimEnd('/');
                    var last = trimmed.LastIndexOf('/');
                    if (last < 0) return string.Empty;
                    var f = trimmed[(last + 1)..].Trim();
                    return f.StartsWith("RUN:", StringComparison.OrdinalIgnoreCase) ? f[4..] : f;
                })
                .Where(r => !string.IsNullOrEmpty(r))
                .Distinct()
                .ToList();
        }

        private async Task AddSelectedRunAsync()
        {
            if (string.IsNullOrEmpty(SelectedRunToAdd) || string.IsNullOrEmpty(_dssFile)) return;
            if (!File.Exists(_dssFile)) return;

            string runName = SelectedRunToAdd;

            // Don't add a run that's already showing, whether auto-matched or manual
            bool alreadyShown =
                _autoRuns.Any(r => string.Equals(r.RunName, runName, StringComparison.OrdinalIgnoreCase)) ||
                _manualRuns.Any(r => string.Equals(r.RunName, runName, StringComparison.OrdinalIgnoreCase));

            if (alreadyShown) return;

            try
            {
                var records = await System.Threading.Tasks.Task.Run(() =>
                    DssHydrographReader.ReadFlow(_dssFile, runName));

                if (records.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"AddRun: '{runName}' has no records, skipping.");
                    return;
                }

                _manualRuns.Add((runName, records));
                await RebuildPlotAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AddRun error for '{runName}': {ex.Message}");
            }
        }

        private async Task ClearManualRunsAsync()
        {
            _manualRuns.Clear();
            await RebuildPlotAsync();
        }

        private async Task RebuildPlotAsync()
        {
            var plot = BuildPlot(_autoRuns, _manualRuns);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                PlotModel = plot);
        }

        private PlotModel BuildEmptyPlot() => BuildPlot(null, null);

        private static PlotModel BuildPlot(
            List<(double ReturnPeriod, string RunName, List<DssHydrographReader.HydrographRecord> Records)>? autoRuns,
            List<(string RunName, List<DssHydrographReader.HydrographRecord> Records)>? manualRuns)
        {
            var model = new PlotModel
            {
                TextColor = OxyColorPalette.Colors["TextAxis"],
                PlotAreaBorderColor = OxyColorPalette.Colors["DimGray"],
                PlotAreaBorderThickness = new OxyThickness(1),
                PlotMargins = new OxyThickness(38, 5, 5, 40),
                DefaultFontSize = 10,
            };

            var xAxis = new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Title = "TIME (hr)",
                Minimum = 0,
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColorPalette.Colors["DimGray"],
                MinorGridlineStyle = LineStyle.Dot,
                MinorGridlineColor = OxyColorPalette.Colors["DimGray"],
                TextColor = OxyColorPalette.Colors["TextAxis"],
                TitleColor = OxyColorPalette.Colors["TextAxis"],
                FontSize = 10,
                TitleFontSize = 10,
                IsZoomEnabled = false,
                IsPanEnabled = false,
            };

            var yAxis = new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "22 DISCHARGE (cfs)",
                Minimum = 0,
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColorPalette.Colors["DimGray"],
                MinorGridlineStyle = LineStyle.Dot,
                MinorGridlineColor = OxyColorPalette.Colors["DimGray"],
                TextColor = OxyColorPalette.Colors["TextAxis"],
                TitleColor = OxyColorPalette.Colors["TextAxis"],
                FontSize = 10,
                TitleFontSize = 10,
                IsZoomEnabled = false,
                IsPanEnabled = false,
            };

            model.Axes.Add(xAxis);
            model.Axes.Add(yAxis);

            int colorIndex = 0;

            // Auto-matched runs first — colored by return-period order, same as before
            if (autoRuns is { Count: > 0 })
            {
                foreach (var (rp, _, records) in autoRuns)
                {
                    var color = RunColors[colorIndex % RunColors.Length];
                    colorIndex++;

                    AddSeries(model, $"{(int)rp}-YR", color, records);
                }
            }

            // Manually added runs — sequential colors continuing from where
            // the auto-matched runs left off, using the run's own name as the title
            if (manualRuns is { Count: > 0 })
            {
                foreach (var (runName, records) in manualRuns)
                {
                    var color = RunColors[colorIndex % RunColors.Length];
                    colorIndex++;

                    AddSeries(model, runName, color, records);
                }
            }

            model.Legends.Add(new Legend
            {
                LegendPosition = LegendPosition.TopLeft,
                LegendPlacement = LegendPlacement.Inside,
                LegendFontSize = 9,
                LegendBackground = OxyColor.FromArgb(180, 0, 0, 0),
                LegendBorder = OxyColorPalette.Colors["DimGray"],
                LegendBorderThickness = 1,
            });

            return model;
        }

        private static void AddSeries(PlotModel model, string title, OxyColor color,
            List<DssHydrographReader.HydrographRecord> records)
        {
            if (records.Count == 0) return;

            var startTime = records[0].Time;

            var series = new LineSeries
            {
                Title = title,
                Color = color,
                StrokeThickness = 1.5,
            };

            foreach (var r in records)
                series.Points.Add(new DataPoint((r.Time - startTime).TotalHours, r.Value));

            model.Series.Add(series);
        }
    }
}