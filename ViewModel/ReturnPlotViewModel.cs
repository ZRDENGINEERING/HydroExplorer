using HydroExplorer.Helpers;
using HydroExplorer.Themes;
using HydroExplorer.Utils;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using System.IO;

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
        ];

        public ReturnPlotViewModel()
        {
            PlotModel = BuildEmptyPlot();

            // Reload when user changes DSS file or run in Hydrology tab
            EventBus.DssRunSelected += (dssPath, _) => LoadAsync(dssPath);
        }

        private async void LoadAsync(string dssFile)
        {
            if (!File.Exists(dssFile)) return;
            try
            {
                // Get all run names from the file
                var allPaths = await System.Threading.Tasks.Task.Run(() =>
                    DssHyetographReader.GetAllPaths(dssFile));

                var runNames = allPaths
                    .Select(p => {
                        var trimmed = p.TrimEnd('/');
                        var last = trimmed.LastIndexOf('/');
                        if (last < 0) return string.Empty;
                        var f = trimmed[(last + 1)..].Trim();
                        return f.StartsWith("RUN:", StringComparison.OrdinalIgnoreCase) ? f[4..] : f;
                    })
                    .Where(r => !string.IsNullOrEmpty(r))
                    .Distinct()
                    .ToList();

                // Match return period suffixes against available run names
                var allRecords = new List<(double ReturnPeriod, List<DssHydrographReader.HydrographRecord> Records)>();

                foreach (var (suffix, rp) in ReturnPeriods)
                {
                    var matchedRun = runNames.FirstOrDefault(r =>
                        r.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

                    if (matchedRun == null) continue;

                    var records = await System.Threading.Tasks.Task.Run(() =>
                        DssHydrographReader.ReadFlow(dssFile, matchedRun));

                    if (records.Count > 0)
                    {
                        allRecords.Add((rp, records));
                        //System.Diagnostics.Debug.WriteLine(
                        //    $"ReturnPlot: {rp}yr run='{matchedRun}' records={records.Count} " +
                        //    $"peak={records.Max(r => r.Value):F0}");
                    }
                }

                if (allRecords.Count > 0)
                {
                    var plot = BuildPlot(allRecords);
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        PlotModel = plot);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ReturnPlotViewModel error: {ex.Message}");
            }
        }

        private PlotModel BuildEmptyPlot() => BuildPlot(null);

        private static PlotModel BuildPlot(
            List<(double ReturnPeriod, List<DssHydrographReader.HydrographRecord> Records)>? runs = null)
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
                Title = "DISCHARGE (cfs)",
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

            if (runs is { Count: > 0 })
            {
                for (int i = 0; i < runs.Count; i++)
                {
                    var (rp, records) = runs[i];
                    var color = RunColors[i % RunColors.Length];
                    var startTime = records[0].Time;

                    var series = new LineSeries
                    {
                        Title = $"{(int)rp}-YR",
                        Color = color,
                        StrokeThickness = 1.5,
                    };

                    foreach (var r in records)
                        series.Points.Add(new DataPoint((r.Time - startTime).TotalHours, r.Value));

                    model.Series.Add(series);
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
    }
}