using HydroExplorer.Themes;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HydroExplorer.ViewModel
{
    public class LP3Record
    {
        public double ReturnInterval { get; set; }
        public double PeakDischarge { get; set; }
        public double UpperCI { get; set; }
        public double LowerCI { get; set; }
    }

    public class LP3PlotViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private PlotModel _plotModelLP3 = new();
        public PlotModel PlotModelLP3
        {
            get => _plotModelLP3;
            set
            {
                if (_plotModelLP3 != null)
                    _plotModelLP3.InvalidatePlot(false);
                _plotModelLP3 = value;
                OnPropertyChanged();
            }
        }

        public LP3PlotViewModel()
        {
            LoadDummyData();
        }

        public void LoadDummyData()
        {
            var seriesA = new List<LP3Record>
            {
                new() { ReturnInterval = 1.05,  PeakDischarge = 1480,  UpperCI = 1640,  LowerCI = 1320 },
                new() { ReturnInterval = 1.25,  PeakDischarge = 2050,  UpperCI = 2220,  LowerCI = 1870 },
                new() { ReturnInterval = 2,     PeakDischarge = 3030,  UpperCI = 3260,  LowerCI = 2820 },
                new() { ReturnInterval = 5,     PeakDischarge = 4750,  UpperCI = 5190,  LowerCI = 4390 },
                new() { ReturnInterval = 10,    PeakDischarge = 6160,  UpperCI = 6860,  LowerCI = 5610 },
                new() { ReturnInterval = 25,    PeakDischarge = 8280,  UpperCI = 9460,  LowerCI = 7390 },
                new() { ReturnInterval = 50,    PeakDischarge = 10100, UpperCI = 11800, LowerCI = 8910 },
                new() { ReturnInterval = 100,   PeakDischarge = 12200, UpperCI = 14500, LowerCI = 10600 },
                new() { ReturnInterval = 200,   PeakDischarge = 14600, UpperCI = 17700, LowerCI = 12500 },
                new() { ReturnInterval = 500,   PeakDischarge = 18300, UpperCI = 22600, LowerCI = 15300 },
            };

            var seriesB = new List<LP3Record>
            {
                new() { ReturnInterval = 1.05,  PeakDischarge = 1500,  UpperCI = 1650,  LowerCI = 1340 },
                new() { ReturnInterval = 1.25,  PeakDischarge = 2050,  UpperCI = 2220,  LowerCI = 1870 },
                new() { ReturnInterval = 2,     PeakDischarge = 3020,  UpperCI = 3240,  LowerCI = 2800 },
                new() { ReturnInterval = 5,     PeakDischarge = 4740,  UpperCI = 5180,  LowerCI = 4380 },
                new() { ReturnInterval = 10,    PeakDischarge = 6170,  UpperCI = 6870,  LowerCI = 5620 },
                new() { ReturnInterval = 25,    PeakDischarge = 8350,  UpperCI = 9540,  LowerCI = 7450 },
                new() { ReturnInterval = 50,    PeakDischarge = 10300, UpperCI = 12000, LowerCI = 9020 },
                new() { ReturnInterval = 100,   PeakDischarge = 12500, UpperCI = 14800, LowerCI = 10800 },
                new() { ReturnInterval = 200,   PeakDischarge = 15000, UpperCI = 18200, LowerCI = 12800 },
                new() { ReturnInterval = 500,   PeakDischarge = 18900, UpperCI = 23500, LowerCI = 15800 },
            };

            PlotModelLP3 = CreatePlotLP3(seriesA, seriesB);
        }

        public void LoadData(List<LP3Record> seriesA, List<LP3Record> seriesB)
        {
            PlotModelLP3 = CreatePlotLP3(seriesA, seriesB);
        }

        private static PlotModel CreatePlotLP3(List<LP3Record> seriesA, List<LP3Record> seriesB)
        {
            var model = new PlotModel
            {
                TitlePadding = 0,
                TitleFontSize = 12,
                DefaultFontSize = 10,
                TextColor = OxyColorPalette.Colors["TextAxis"],
                PlotMargins = new OxyThickness(50, 10, 10, 40),
                PlotAreaBorderColor = OxyColorPalette.Colors["DimGray"],
                PlotAreaBorderThickness = new OxyThickness(1),
            };

            var xAxis = new LogarithmicAxis
            {
                Position = AxisPosition.Bottom,
                Title = "Return Period (years)",
                Minimum = 1,
                Maximum = 600,
                AxislineStyle = LineStyle.Solid,
                AxislineColor = OxyColorPalette.Colors["DimGray"],
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColorPalette.Colors["DimGray"],
                MinorGridlineStyle = LineStyle.Dot,
                MinorGridlineColor = OxyColorPalette.Colors["DimGray"],
                TextColor = OxyColorPalette.Colors["TextAxis"],
                FontSize = 10,
            };

            var yAxis = new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "Peak Discharge (cfs)",
                AxislineStyle = LineStyle.Solid,
                AxislineColor = OxyColorPalette.Colors["DimGray"],
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColorPalette.Colors["DimGray"],
                MinorGridlineStyle = LineStyle.Dot,
                MinorGridlineColor = OxyColorPalette.Colors["DimGray"],
                TextColor = OxyColorPalette.Colors["TextAxis"],
                FontSize = 10,
            };

            model.Axes.Add(xAxis);
            model.Axes.Add(yAxis);

            // Series A — fitted line
            var lineA = new LineSeries
            {
                Title = "Plan A",
                Color = OxyColors.GreenYellow,
                StrokeThickness = 2,
                MarkerType = MarkerType.Circle,
                MarkerSize = 4,
                MarkerFill = OxyColors.GreenYellow,
            };

            // Series A — confidence interval
            var ciUpperA = new LineSeries
            {
                Title = "95% CI (A)",
                Color = OxyColor.FromAColor(120, OxyColors.GreenYellow),
                StrokeThickness = 1,
                LineStyle = LineStyle.Dash,
            };
            var ciLowerA = new LineSeries
            {
                Title = string.Empty,
                Color = OxyColor.FromAColor(120, OxyColors.GreenYellow),
                StrokeThickness = 1,
                LineStyle = LineStyle.Dash,
            };

            // Series B — fitted line
            var lineB = new LineSeries
            {
                Title = "Plan B",
                Color = OxyColors.SteelBlue,
                StrokeThickness = 2,
                MarkerType = MarkerType.Circle,
                MarkerSize = 4,
                MarkerFill = OxyColors.SteelBlue,
            };

            // Series B — confidence interval
            var ciUpperB = new LineSeries
            {
                Title = "95% CI (B)",
                Color = OxyColor.FromAColor(120, OxyColors.SteelBlue),
                StrokeThickness = 1,
                LineStyle = LineStyle.Dash,
            };
            var ciLowerB = new LineSeries
            {
                Title = string.Empty,
                Color = OxyColor.FromAColor(120, OxyColors.SteelBlue),
                StrokeThickness = 1,
                LineStyle = LineStyle.Dash,
            };

            foreach (var r in seriesA.OrderBy(r => r.ReturnInterval))
            {
                lineA.Points.Add(new DataPoint(r.ReturnInterval, r.PeakDischarge));
                ciUpperA.Points.Add(new DataPoint(r.ReturnInterval, r.UpperCI));
                ciLowerA.Points.Add(new DataPoint(r.ReturnInterval, r.LowerCI));
            }

            foreach (var r in seriesB.OrderBy(r => r.ReturnInterval))
            {
                lineB.Points.Add(new DataPoint(r.ReturnInterval, r.PeakDischarge));
                ciUpperB.Points.Add(new DataPoint(r.ReturnInterval, r.UpperCI));
                ciLowerB.Points.Add(new DataPoint(r.ReturnInterval, r.LowerCI));
            }

            model.Series.Add(lineA);
            model.Series.Add(ciUpperA);
            model.Series.Add(ciLowerA);
            model.Series.Add(lineB);
            model.Series.Add(ciUpperB);
            model.Series.Add(ciLowerB);

            model.Legends.Add(new Legend
            {
                LegendPosition = LegendPosition.TopLeft,
                LegendPlacement = LegendPlacement.Inside,
                LegendFontSize = 10,
            });

            return model;
        }
    }
}