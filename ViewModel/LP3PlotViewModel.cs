using HydroExplorer.Helpers;
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
                _plotModelLP3?.InvalidatePlot(false);
                _plotModelLP3 = value;
                OnPropertyChanged();
            }
        }

        private string _planNameA = "Plan A";
        private string _planNameB = "Plan B";

        public LP3PlotViewModel()
        {
            LoadDummyData();

            EventBus.PlanNamesChanged += (nameA, nameB) =>
            {
                _planNameA = string.IsNullOrEmpty(nameA) ? "Plan A" : nameA;
                _planNameB = string.IsNullOrEmpty(nameB) ? "Plan B" : nameB;
                System.Windows.Application.Current.Dispatcher.Invoke(LoadDummyData);
            };
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

            PlotModelLP3 = CreatePlotLP3(seriesA, seriesB, _planNameA, _planNameB);
        }

        public void LoadData(List<LP3Record> seriesA, List<LP3Record> seriesB)
            => PlotModelLP3 = CreatePlotLP3(seriesA, seriesB, _planNameA, _planNameB);

        private static PlotModel CreatePlotLP3(
            List<LP3Record> seriesA, List<LP3Record> seriesB,
            string nameA, string nameB)
        {
            var model = new PlotModel
            {
                TitlePadding = 0,
                TitleFontSize = 12,
                DefaultFontSize = 10,
                TextColor = OxyColorPalette.Colors["TextAxis"],
                PlotMargins = new OxyThickness(45, 5, 5, 40),
                PlotAreaBorderColor = OxyColorPalette.Colors["DimGray"],
                PlotAreaBorderThickness = new OxyThickness(1),
            };

            var xAxis = new LogarithmicAxis
            {
                Position = AxisPosition.Bottom,
                Title = "RETURN PERIOD (yr)",
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
                Title = "PEAK DISCHARGE (cfs)",
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

            var lineA = MakeLine(nameA, OxyColors.GreenYellow, 2, LineStyle.Solid);
            var ciUpperA = MakeLine($"95% CI ({nameA})", OxyColor.FromAColor(120, OxyColors.GreenYellow), 1, LineStyle.Dash);
            var ciLowerA = MakeLine(string.Empty, OxyColor.FromAColor(120, OxyColors.GreenYellow), 1, LineStyle.Dash);
            var lineB = MakeLine(nameB, OxyColors.SteelBlue, 2, LineStyle.Solid);
            var ciUpperB = MakeLine($"95% CI ({nameB})", OxyColor.FromAColor(120, OxyColors.SteelBlue), 1, LineStyle.Dash);
            var ciLowerB = MakeLine(string.Empty, OxyColor.FromAColor(120, OxyColors.SteelBlue), 1, LineStyle.Dash);

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

        private static LineSeries MakeLine(string title, OxyColor color,
            double thickness, LineStyle style) => new()
            {
                Title = title,
                Color = color,
                StrokeThickness = thickness,
                LineStyle = style,
                MarkerType = thickness > 1 ? MarkerType.Circle : MarkerType.None,
                MarkerSize = 4,
                MarkerFill = color,
            };
    }
}