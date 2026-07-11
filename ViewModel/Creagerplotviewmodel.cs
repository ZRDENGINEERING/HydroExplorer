using HydroExplorer.Helpers;
using HydroExplorer.Themes;
using HydroExplorer.View;
using Microsoft.Extensions.DependencyInjection;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using System.ComponentModel;
using System.Runtime.CompilerServices;



namespace HydroExplorer.ViewModel
{
    /// <summary>
    /// Plots the Creager envelope curve (regional peak-discharge envelope, per Creager,
    /// Justin &amp; Hinds 1945) on log-log axes, with the current project's drainage area
    /// and peak discharge overlaid as a single point so the user can see how their
    /// project's flood peak compares to historical regional envelope behavior.
    ///
    /// Formula: Q = C * 46 * A^(0.894 * A^-0.048)
    /// where Q = peak discharge (cfs), A = drainage area (sq mi), C = envelope coefficient.
    /// Several reference C curves are drawn (commonly 100, 60, 30 — representing
    /// decreasing envelope severity) so the project's point can be read against them.
    ///
    /// Data sources:
    ///   - Drainage area: ProjectSettings.GageDrainageAreaSqMi (USGS nearest-gage
    ///     drainage area, already resolved/cached per project).
    ///   - Peak discharge: QTotalA at the most downstream cross-section (lowest
    ///     numeric RiverSta) for the currently selected profile, read from the same
    ///     WSELTableOxy data PlotViewModel already loads from the HEC-RAS plan HDF.
    /// </summary>
    public class CreagerPlotViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private PlotModel _plotModel = new();
        public PlotModel PlotModel
        {
            get => _plotModel;
            set
            {
                _plotModel?.InvalidatePlot(false);
                _plotModel = value;
                OnPropertyChanged();
            }
        }

        // OxyPlot's PlotView claims exclusive ownership of whatever PlotModel
        // instance it's bound to — binding the same PlotModel to two PlotViews at
        // once (Main-tab full-width view + Charts/Info-tab half-width view) throws
        // "This PlotModel is already in use by some other PlotView control.",
        // even when only one is visible at a time, since both controls stay attached
        // regardless of Visibility. PlotModelChartsTab is a second, independently
        // rebuilt instance with identical content, mirroring the old PlotViewModel's
        // PlotModel/PlotModelChartsTab split for the same reason.
        private PlotModel _plotModelChartsTab = new();
        public PlotModel PlotModelChartsTab
        {
            get => _plotModelChartsTab;
            set
            {
                _plotModelChartsTab?.InvalidatePlot(false);
                _plotModelChartsTab = value;
                OnPropertyChanged();
            }
        }

        // Standard reference envelope coefficients — adjust if your region's
        // governing envelope uses different C values.
        private static readonly double[] EnvelopeCoefficients = [100, 60, 30];

        private IUserSettingsRepo? _settingsRepo;
        private bool _dataLoaded;

        private double? _projectDrainageAreaSqMi;
        private double? _projectPeakDischargeCfs;
        private string? _projectLabel;

        public CreagerPlotViewModel()
        {
            PlotModel = CreateEnvelopeOnlyPlot();
            PlotModelChartsTab = CreateEnvelopeOnlyPlot();
        }

        /// <summary>
        /// Loads the current project's drainage area (from ProjectSettings) and peak
        /// discharge (from WSELTableOxy data for the given profile) and rebuilds the
        /// plot with that point overlaid on the envelope curves. Safe to call
        /// repeatedly — only the first call per instance actually loads project
        /// settings; subsequent calls just refresh the overlay point for a new profile
        /// selection via RefreshPoint.
        /// </summary>
        public async Task LoadDataAsync(List<WSELTableOxy>? wselData, string? selectedProfile)
        {
            _settingsRepo ??= App.ServiceProvider.GetRequiredService<IUserSettingsRepo>();

            if (!_dataLoaded)
            {
                _dataLoaded = true;
                try
                {
                    var settings = await _settingsRepo.GetSettings();
                    if (!string.IsNullOrEmpty(settings.ProjPath) &&
                        settings.Projects.TryGetValue(settings.ProjPath, out var proj))
                    {
                        _projectDrainageAreaSqMi = proj.GageDrainageAreaSqMi;
                        _projectLabel = !string.IsNullOrEmpty(proj.ProjName)
                            ? proj.ProjName
                            : "Project";
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"CreagerPlotViewModel.LoadDataAsync error: {ex.Message}");
                }
            }

            RefreshPoint(wselData, selectedProfile);
        }

        /// <summary>
        /// Recomputes peak discharge for the given profile and rebuilds the plot.
        /// Call this whenever the selected profile or WSEL data changes, without
        /// re-fetching project settings.
        /// </summary>
        public void RefreshPoint(List<WSELTableOxy>? wselData, string? selectedProfile)
        {
            _projectPeakDischargeCfs = ResolvePeakDischarge(wselData, selectedProfile);

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                bool hasPoint = _projectDrainageAreaSqMi is > 0 && _projectPeakDischargeCfs is > 0;

                PlotModel = hasPoint
                    ? CreatePlotWithProjectPoint(
                        _projectDrainageAreaSqMi!.Value,
                        _projectPeakDischargeCfs!.Value,
                        _projectLabel ?? "Project")
                    : CreateEnvelopeOnlyPlot();

                // Independently-built instance — never the same object as PlotModel
                // above, since both can be attached to live PlotViews at once.
                PlotModelChartsTab = hasPoint
                    ? CreatePlotWithProjectPoint(
                        _projectDrainageAreaSqMi!.Value,
                        _projectPeakDischargeCfs!.Value,
                        _projectLabel ?? "Project")
                    : CreateEnvelopeOnlyPlot();
            });
        }

        /// <summary>
        /// QTotalA at the most downstream cross-section (lowest numeric RiverSta) for
        /// the selected profile. Falls back to the first profile present if
        /// selectedProfile doesn't match any row (mirrors the "defaulting to 0" pattern
        /// used elsewhere when reading HDF profile data).
        /// </summary>
        private static double? ResolvePeakDischarge(List<WSELTableOxy>? wselData, string? selectedProfile)
        {
            if (wselData is null || wselData.Count == 0) return null;

            var profileRows = !string.IsNullOrEmpty(selectedProfile)
                ? wselData.Where(r => string.Equals(r.Profile, selectedProfile, StringComparison.OrdinalIgnoreCase)).ToList()
                : [];

            if (profileRows.Count == 0)
                profileRows = wselData.Where(r => r.Profile == wselData[0].Profile).ToList();

            if (profileRows.Count == 0) return null;

            WSELTableOxy? downstream = null;
            double downstreamSta = double.MaxValue;

            foreach (var row in profileRows)
            {
                string cleaned = row.RiverSta?.Replace(",", "").Trim() ?? string.Empty;
                if (!double.TryParse(cleaned, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double sta))
                    continue;

                if (sta < downstreamSta)
                {
                    downstreamSta = sta;
                    downstream = row;
                }
            }

            if (downstream is null || float.IsNaN(downstream.QTotalA)) return null;

            return downstream.QTotalA;
        }

        private static PlotModel CreateEnvelopeOnlyPlot() => BuildPlot(null, null, null);

        private static PlotModel CreatePlotWithProjectPoint(double drainageAreaSqMi, double peakDischargeCfs, string label) =>
            BuildPlot(drainageAreaSqMi, peakDischargeCfs, label);

        private static PlotModel BuildPlot(double? projectArea, double? projectQ, string? projectLabel)
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
                Title = "DRAINAGE AREA (sq mi)",
                Minimum = 0.001,
                Maximum = 1001,
                AxislineStyle = LineStyle.Solid,
                AxislineColor = OxyColorPalette.Colors["DimGray"],
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColorPalette.Colors["DimGray"],
                MinorGridlineStyle = LineStyle.Dot,
                MinorGridlineColor = OxyColorPalette.Colors["DimGray"],
                TextColor = OxyColorPalette.Colors["TextAxis"],
                FontSize = 10,
            };

            var yAxis = new LogarithmicAxis
            {
                Position = AxisPosition.Left,
                Title = "PEAK DISCHARGE (cfs)",
                Minimum = 0.1,
                Maximum = 500_001,
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

            // Same two-color accent pairing pattern as LP3 (GreenYellow/SteelBlue),
            // extended with a third tone for the third envelope curve.
            var palette = new[] { OxyColors.GreenYellow, OxyColors.SteelBlue, OxyColors.DarkOrange };

            for (int c = 0; c < EnvelopeCoefficients.Length; c++)
            {
                double coeff = EnvelopeCoefficients[c];
                var series = MakeLine($"C = {coeff:0}", palette[c % palette.Length], 1, LineStyle.Solid);

                // Sample log-spaced drainage areas across the axis range.
                const int sampleCount = 100;
                double logMin = Math.Log10(xAxis.Minimum);
                double logMax = Math.Log10(xAxis.Maximum);

                for (int i = 0; i <= sampleCount; i++)
                {
                    double logA = logMin + (logMax - logMin) * i / sampleCount;
                    double area = Math.Pow(10, logA);
                    double q = CreagerQ(coeff, area);
                    series.Points.Add(new DataPoint(area, q));
                }

                model.Series.Add(series);
            }

            if (projectArea is > 0 && projectQ is > 0)
            {
                var pointSeries = new ScatterSeries
                {
                    Title = projectLabel ?? "Project",
                    MarkerType = MarkerType.Circle,
                    MarkerSize = 6,
                    MarkerFill = OxyColorPalette.Colors["TextAxis"],
                    MarkerStroke = OxyColors.White,
                    MarkerStrokeThickness = 1
                };
                pointSeries.Points.Add(new ScatterPoint(projectArea.Value, projectQ.Value));
                model.Series.Add(pointSeries);
            }

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
                MarkerType = MarkerType.None,
            };

        /// <summary>
        /// Creager envelope formula: Q = C * 46 * A^(0.894 * A^-0.048)
        /// </summary>
        private static double CreagerQ(double coefficient, double drainageAreaSqMi) =>
            coefficient * 46.0 * Math.Pow(drainageAreaSqMi, 0.894 * Math.Pow(drainageAreaSqMi, -0.048));
    }
}