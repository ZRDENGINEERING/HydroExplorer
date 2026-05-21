using HydroExplorer.Utils;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.Legends;
using HydroExplorer.Themes;
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

        private static readonly (string Run, double ReturnPeriod)[] Runs =
        [
            ("BOG_2YR",   2),
            ("BOG_5YR",   5),
            ("BOG_10YR",  10),
            ("BOG_25YR",  25),
            ("BOG_50YR",  50),
            ("BOG_100YR", 100),
            ("BOG_500YR", 500),
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
            LoadAsync();
        }


        private async void LoadAsync()
        {
            try
            {
                var dssFile = @"C:\Temp\199805003 Bart Test\Boggy\Boggy.dss";
                if (!File.Exists(dssFile)) return;

                var allRecords = new List<(double ReturnPeriod, List<DssHyetographReader.HyetographRecord> Records)>();

                foreach (var (run, rp) in Runs)
                {
                    var records = await System.Threading.Tasks.Task.Run(() =>
                        DssHyetographReader.ReadByPartB(dssFile, "FLOW", run));

                    if (records.Count > 0)
                    {
                        allRecords.Add((rp, records));
                        //System.Diagnostics.Debug.WriteLine($"Return period {rp}yr: {records.Count} records, peak: {records.Max(r => r.Value):F0} cfs");
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
    List<(double ReturnPeriod, List<DssHyetographReader.HyetographRecord> Records)>? runs = null)
        {
            var model = new PlotModel
            {
                TextColor = OxyColorPalette.Colors["TextAxis"],
                PlotAreaBorderColor = OxyColorPalette.Colors["DimGray"],
                PlotAreaBorderThickness = new OxyThickness(1),
                PlotMargins = new OxyThickness(70, 10, 10, 40),
                DefaultFontSize = 10,
            };

            var xAxis = new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Title = "TIME IN HOURS",
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
                Title = "DISCHARGE (CFS)",
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
                        LineStyle = LineStyle.Solid,
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