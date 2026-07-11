using HydroExplorer.Themes;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HydroExplorer.ViewModel
{
    /// <summary>
    /// One recurrence interval's Omega EM regression coefficients, per TxDOT
    /// Hydraulic Design Manual Table 4-4:
    ///   QT = P^b * S^c * 10^(d*Omega + e - f*A^g)
    /// where P = mean annual precipitation (in), S = main channel slope (ft/ft),
    /// Omega = terrain/climate index, A = drainage area (sq mi).
    /// </summary>
    public record OmegaCoefficients(
        double ReturnInterval,
        double B_pExp, double C_sExp, double D_omegaCoef, double E_const,
        double F_aCoef, double G_aExp);

    public class OmegaRecord
    {
        public double ReturnInterval { get; set; }
        public double PeakDischarge { get; set; }
    }

    public class OmegaPlotViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private PlotModel _plotModelOmegaEm = new();
        public PlotModel PlotModelOmegaEm
        {
            get => _plotModelOmegaEm;
            set
            {
                _plotModelOmegaEm?.InvalidatePlot(false);
                _plotModelOmegaEm = value;
                OnPropertyChanged();
            }
        }

        // TxDOT Hydraulic Design Manual, Table 4-4.
        private static readonly List<OmegaCoefficients> _coefficients =
        [
            new(2,   1.398, 0.270, 0.776, 50.98, 50.30,  -0.0058),
            new(5,   1.308, 0.372, 0.885, 16.62, 15.32,  -0.0215),
            new(10,  1.203, 0.403, 0.918, 13.62, 11.97,  -0.0289),
            new(25,  1.140, 0.446, 0.945, 11.79, 9.819,  -0.0374),
            new(50,  1.105, 0.476, 0.961, 11.17, 8.997,  -0.0424),
            new(100, 1.071, 0.507, 0.969, 10.82, 8.448,  -0.0467),
            new(500, 0.988, 0.569, 0.976, 10.40, 7.605,  -0.0554),
        ];

        /// <summary>
        /// Computes QT for each TxDOT-tabulated recurrence interval from
        /// watershed inputs, per Table 4-4:
        ///   QT = P^b * S^c * 10^(d*Omega + e - f*A^g)
        /// </summary>
        /// <param name="drainageAreaSqMi">A — drainage area, sq mi</param>
        /// <param name="mainChannelSlope">S — main channel slope, ft/ft</param>
        /// <param name="meanAnnualPrecipIn">P — mean annual precipitation, in</param>
        /// <param name="omega">Ω — terrain/climate index</param>
        public List<OmegaRecord> ComputeOmega(
            double drainageAreaSqMi, double mainChannelSlope,
            double meanAnnualPrecipIn, double omega)
        {
            var results = new List<OmegaRecord>();

            foreach (var c in _coefficients)
            {
                double exponent = c.D_omegaCoef * omega + c.E_const
                    - c.F_aCoef * Math.Pow(drainageAreaSqMi, c.G_aExp);

                double q = Math.Pow(meanAnnualPrecipIn, c.B_pExp)
                    * Math.Pow(mainChannelSlope, c.C_sExp)
                    * Math.Pow(10, exponent);

                results.Add(new OmegaRecord
                {
                    ReturnInterval = c.ReturnInterval,
                    PeakDischarge = q
                });
            }

            return results;
        }

        public void LoadData(double drainageAreaSqMi, double mainChannelSlope,
            double meanAnnualPrecipIn, double omega)
        {
            var records = ComputeOmega(drainageAreaSqMi, mainChannelSlope, meanAnnualPrecipIn, omega);
            PlotModelOmegaEm = CreatePlotOmega(records);
        }

        private static PlotModel CreatePlotOmega(List<OmegaRecord> records)
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
                Maximum = 601,
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

            var line = new LineSeries
            {
                Title = "Omega EM",
                Color = OxyColors.Orange,
                StrokeThickness = 1,
                MarkerType = MarkerType.Circle,
                MarkerSize = 2,
                MarkerFill = OxyColors.Orange,
            };

            foreach (var r in records.OrderBy(r => r.ReturnInterval))
                line.Points.Add(new DataPoint(r.ReturnInterval, r.PeakDischarge));

            model.Series.Add(line);

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