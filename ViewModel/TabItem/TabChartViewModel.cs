using HydroExplorer.Core;
using HydroExplorer.Helpers;
using HydroExplorer.Themes;
using HydroExplorer.Utils;
using HydroExplorer.View;
using Microsoft.Extensions.DependencyInjection;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using System.IO;
using System.Windows.Input;
using System.Windows;


namespace HydroExplorer.ViewModel.TabItem
{
    public class TabChartViewModel : TabViewModelBase
    {
        // ── Properties ───────────────────────────────────────────────────────

        private double _rainfallTotal;
        private double _lossTotal;
        private double _rainfallExcessTotal;
        private double _initialLoss;
        private double _infiltrationIndex;

        public double RainfallTotal { get => _rainfallTotal; set { _rainfallTotal = value; OnPropertyChanged(); } }
        public double LossTotal { get => _lossTotal; set { _lossTotal = value; OnPropertyChanged(); } }
        public double RainfallExcessTotal { get => _rainfallExcessTotal; set { _rainfallExcessTotal = value; OnPropertyChanged(); } }
        public double InitialLoss { get => _initialLoss; set { _initialLoss = value; OnPropertyChanged(); } }
        public double InfiltrationIndex { get => _infiltrationIndex; set { _infiltrationIndex = value; OnPropertyChanged(); } }

        private string _header = "Chart";
        public override string Header
        {
            get => _header;
            set { _header = value; OnPropertyChanged(nameof(Header)); }
        }


        public Visibility ElevationPlotVisibility =>
            ElevationDataAvailable ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ElevationWatermarkVisibility =>
            ElevationDataAvailable ? Visibility.Collapsed : Visibility.Visible;

        private bool _elevationDataAvailable = true;
        public bool ElevationDataAvailable
        {
            get => _elevationDataAvailable;
            set
            {
                _elevationDataAvailable = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ElevationPlotVisibility));
                OnPropertyChanged(nameof(ElevationWatermarkVisibility));
            }
        }





        private PlotModel? _elevationPlot;
        private PlotModel? _hydrographPlot;
        private PlotModel? _hyetographPlot;

        public PlotModel? ElevationPlot
        {
            get => _elevationPlot;
            set
            {
                var old = _elevationPlot;
                _elevationPlot = value;
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                { old?.InvalidatePlot(false); OnPropertyChanged(); });
            }
        }





        public PlotModel? HydrographPlot
        {
            get => _hydrographPlot;
            set
            {
                var old = _hydrographPlot;
                _hydrographPlot = value;
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                { old?.InvalidatePlot(false); OnPropertyChanged(); });
            }
        }

        public PlotModel? HyetographPlot
        {
            get => _hyetographPlot;
            set { _hyetographPlot?.InvalidatePlot(false); _hyetographPlot = value; OnPropertyChanged(); }
        }

        public ICommand GenerateReportCommand { get; }

        // ── Constructor ──────────────────────────────────────────────────────

        public TabChartViewModel()
        {
            System.Diagnostics.Debug.WriteLine($"TabChartViewModel CONSTRUCTED — instance {GetHashCode()}");


            ElevationPlot = BuildElevationPlot();
            HydrographPlot = BuildHydrographPlot();
            HyetographPlot = BuildHyetographPlot(null);

            GenerateReportCommand = new RelayCommand(_ => GenerateReport());

            _ = LoadFromSettingsAsync();

            EventBus.DssRunSelected += (dssPath, runName) =>
            {
                System.Diagnostics.Debug.WriteLine($"DssRunSelected received by instance {GetHashCode()}");
                LoadDssDataAsync(dssPath, runName);
            };

        }

        // ── Load ─────────────────────────────────────────────────────────────

        private async Task LoadFromSettingsAsync()
        {
            try
            {
                var settingsRepo = App.ServiceProvider
                    .GetRequiredService<IUserSettingsRepo>();
                var settings = await settingsRepo.GetSettings();

                string projPath = PathHelpers.NormalizeProjKey(settings.LastProjPath);
                if (string.IsNullOrEmpty(projPath) ||
                    !settings.Projects.TryGetValue(projPath, out var proj))
                    return;

                if (string.IsNullOrEmpty(proj.DssPath) || !File.Exists(proj.DssPath))
                    return;

                var allPaths = await Task.Run(() =>
                    DssHyetographReader.GetAllPaths(proj.DssPath));

                var runName = allPaths
                    .Select(p => {
                        var trimmed = p.TrimEnd('/');
                        var last = trimmed.LastIndexOf('/');
                        if (last < 0) return string.Empty;
                        var f = trimmed[(last + 1)..].Trim();
                        return f.StartsWith("RUN:", StringComparison.OrdinalIgnoreCase) ? f[4..] : f;
                    })
                    .Where(r => !string.IsNullOrEmpty(r))
                    .Distinct()
                    .FirstOrDefault() ?? string.Empty;

                if (!string.IsNullOrEmpty(runName))
                    LoadDssDataAsync(proj.DssPath, runName);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadFromSettingsAsync error: {ex.Message}");
            }
        }



        private async void LoadDssDataAsync(string dssFile, string runName)
        {
            System.Diagnostics.Debug.WriteLine($"LoadDssDataAsync: '{dssFile}' run='{runName}'");
            try
            {
                if (!File.Exists(dssFile)) return;

                var sw = System.Diagnostics.Stopwatch.StartNew();

                // One DssReader for the whole batch, held under DssGate for the
                // entire open-through-dispose lifetime. See DssGate for why —
                // heclib's native open-file table is a small, fixed-size, shared
                // per-process resource; even one handle per call site can exceed it
                // if multiple call sites fire concurrently (observed: HydrologyPaneView
                // republishing DssRunSelected multiple times per project switch).
                var (precipRecords, flowRecords, flowBaseRecords, flowDirectRecords,
                     flowUGRecords, flowCumRecords, elevRecords) = await Task.Run(() =>
                     {
                         string? tempPath = null;
                         Utils.DssGate.Enter();
                         try
                         {
                             tempPath = Utils.DssSnapshot.Create(dssFile);
                             using var dss = new Hec.Dss.DssReader(tempPath);

                             var precip = DssHyetographReader.ReadPrecipInc(dss, runName);
                             var flow = DssHydrographReader.ReadByPartC(dss, "FLOW", runName);
                             var flowBase = DssHydrographReader.ReadByPartC(dss, "FLOW-BASE", runName);
                             var flowDirect = DssHydrographReader.ReadByPartC(dss, "FLOW-DIRECT", runName);
                             var flowUG = DssHydrographReader.ReadByPartC(dss, "FLOW-UNIT GRAPH", runName);
                             var flowCum = DssHydrographReader.ReadByPartC(dss, "FLOW-CUMULATIVE", runName);
                             var elev = DssHydrographReader.ReadByPartC(dss, "ELEVATION", runName);
                             var storage = DssHydrographReader.ReadByPartC(dss, "STORAGE", runName);

                             return (precip, flow, flowBase, flowDirect, flowUG, flowCum, elev);
                         }
                         finally
                         {
                             Utils.DssSnapshot.Cleanup(tempPath);
                             Utils.DssGate.Exit();
                         }
                     });

                sw.Stop();
                System.Diagnostics.Debug.WriteLine(
                    $"DSS complete in {sw.ElapsedMilliseconds}ms — " +
                    $"precip:{precipRecords.Count} flow:{flowRecords.Count} " +
                    $"flowBase:{flowBaseRecords.Count} flowDirect:{flowDirectRecords.Count} " +
                    $"flowUG:{flowUGRecords.Count} flowCum:{flowCumRecords.Count} elev:{elevRecords.Count}");

                if (precipRecords.Count > 0)
                {
                    RainfallTotal = precipRecords.Sum(r => r.Value);
                    LossTotal = precipRecords.Sum(r => r.Value * 0.05);
                    RainfallExcessTotal = RainfallTotal - LossTotal;
                    InitialLoss = 1.00;
                    InfiltrationIndex = 0.10;
                    var plot = BuildHyetographPlot(precipRecords);
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                        () => HyetographPlot = plot);
                }

                if (flowRecords.Count > 0 || flowCumRecords.Count > 0)
                {
                    var plot = BuildHydrographPlot(
                        flowRecords.Count > 0 ? flowRecords : flowCumRecords,
                        flowBaseRecords, flowDirectRecords, flowUGRecords);
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                        () => HydrographPlot = plot);
                }

                if (elevRecords.Count > 0)
                {
                    var plot = BuildElevationPlot(elevRecords);
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        ElevationPlot = plot;
                        ElevationDataAvailable = true;
                    });
                }
                else
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        ElevationPlot = BuildElevationPlot(null);
                        ElevationDataAvailable = false;
                    });
                }

            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadDssDataAsync error: {ex.Message}");
            }
        }

        // ── Report ───────────────────────────────────────────────────────────

        private void GenerateReport()
        {
            try
            {
                var path = @"C:\Temp\HydroReport.pdf";
                HydroReportGenerator.GenerateHydrographReport(
                    HydrographPlot!, HyetographPlot!, path,
                    projectName: "Boggy Creek — 100YR",
                    rainfallTotal: RainfallTotal,
                    lossTotal: LossTotal,
                    rainfallExcessTotal: RainfallExcessTotal,
                    initialLoss: InitialLoss,
                    infiltrationIndex: InfiltrationIndex);

                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GenerateReport error: {ex.Message}");
            }
        }

        // ── Plot builders ────────────────────────────────────────────────────

        private static PlotModel BuildHyetographPlot(
            List<DssHyetographReader.HyetographRecord>? records)
        {
            var model = new PlotModel
            {
                TextColor = OxyColorPalette.Colors["TextAxis"],
                PlotAreaBorderColor = OxyColorPalette.Colors["DimGray"],
                PlotAreaBorderThickness = new OxyThickness(1),
                PlotMargins = new OxyThickness(38, 5, 5, 40),
                DefaultFontSize = 10,
            };

            var valueAxis = MakeAxis(AxisPosition.Left, "DEPTH (INCHES PER INTERVAL)");
            valueAxis.Maximum = 0;
            valueAxis.StartPosition = 0;
            valueAxis.EndPosition = 1;
            valueAxis.IsZoomEnabled = false;
            valueAxis.IsPanEnabled = false;
            valueAxis.LabelFormatter = v => Math.Abs(v).ToString("F2");

            var timeAxis = MakeAxis(AxisPosition.Bottom, "TIME IN HOURS");
            timeAxis.Minimum = 0;
            timeAxis.IsZoomEnabled = false;
            timeAxis.IsPanEnabled = false;

            model.Axes.Add(valueAxis);
            model.Axes.Add(timeAxis);

            var rainfallSeries = new LinearBarSeries
            {
                Title = "RAINFALL EXCESS",
                FillColor = OxyColors.LightGray,
                StrokeColor = OxyColorPalette.Colors["DimGray"],
                StrokeThickness = 0.5,
                BarWidth = 1,
                NegativeFillColor = OxyColors.LightGray,
            };

            var lossSeries = new LinearBarSeries
            {
                Title = "LOSS",
                FillColor = OxyColors.DarkRed,
                StrokeColor = OxyColorPalette.Colors["DimGray"],
                StrokeThickness = 0.5,
                BarWidth = 1,
                NegativeFillColor = OxyColors.DarkRed,
            };

            if (records is { Count: > 0 })
            {
                var startTime = records[0].Time;
                var binned = records
                    .GroupBy(r => (int)(r.Time - startTime).TotalHours)
                    .OrderBy(g => g.Key)
                    .Select(g => (Hour: g.Key, Value: g.Sum(r => r.Value)))
                    .ToList();

                foreach (var (hour, value) in binned)
                {
                    rainfallSeries.Points.Add(new DataPoint(hour, -value * 0.95));
                    lossSeries.Points.Add(new DataPoint(hour, -value * 0.05));
                }

                timeAxis.MajorStep = 6;
                timeAxis.MinorStep = 1;
                timeAxis.Maximum = binned.Last().Hour + 1;
            }

            model.Series.Add(rainfallSeries);
            model.Series.Add(lossSeries);
            model.Legends.Add(new Legend
            {
                LegendPosition = LegendPosition.BottomCenter,
                LegendPlacement = LegendPlacement.Inside,
                LegendFontSize = 9,
            });

            return model;
        }

        private static PlotModel BuildHydrographPlot(
            List<DssHydrographReader.HydrographRecord>? flow = null,
            List<DssHydrographReader.HydrographRecord>? flowBase = null,
            List<DssHydrographReader.HydrographRecord>? flowDirect = null,
            List<DssHydrographReader.HydrographRecord>? flowUG = null)
        {
            var model = new PlotModel
            {
                TextColor = OxyColorPalette.Colors["TextAxis"],
                PlotAreaBorderColor = OxyColorPalette.Colors["DimGray"],
                PlotAreaBorderThickness = new OxyThickness(1),
                PlotMargins = new OxyThickness(38, 5, 5, 40),
                DefaultFontSize = 10,
            };

            var xAxis = MakeAxis(AxisPosition.Bottom, "TIME IN HOURS");
            xAxis.Minimum = 0;

            var yAxis = MakeAxis(AxisPosition.Left, "DISCHARGE (CFS)");
            yAxis.Minimum = 0;

            model.Axes.Add(xAxis);
            model.Axes.Add(yAxis);

            double maxHours = 0;

            void AddSeries(List<DssHydrographReader.HydrographRecord>? records,
                           string title, OxyColor color,
                           LineStyle style = LineStyle.Solid)
            {
                if (records is not { Count: > 0 }) return;
                var series = new LineSeries
                {
                    Title = title,
                    Color = color,
                    StrokeThickness = 1.5,
                    LineStyle = style,
                };
                var startTime = records[0].Time;
                foreach (var r in records)
                    series.Points.Add(new DataPoint((r.Time - startTime).TotalHours, r.Value));
                model.Series.Add(series);
                double elapsed = (records.Last().Time - records[0].Time).TotalHours;
                if (elapsed > maxHours) maxHours = elapsed;
            }

            AddSeries(flow, "FLOW", OxyColors.SteelBlue);
            //AddSeries(flowBase, "FLOW-BASE", OxyColors.DarkGreen, LineStyle.Dash);
            //AddSeries(flowDirect, "FLOW-DIRECT", OxyColors.DarkRed, LineStyle.Dash);
            AddSeries(flowUG, "FLOW-UNIT GRAPH", OxyColors.Orange, LineStyle.Dot);

            if (maxHours > 0)
            {
                xAxis.Maximum = maxHours;
                xAxis.MajorStep = 6;
                xAxis.MinorStep = 1;
            }

            model.Legends.Add(new Legend
            {
                LegendPosition = LegendPosition.TopRight,
                LegendPlacement = LegendPlacement.Inside,
                LegendFontSize = 9,
            });

            return model;
        }

        private static PlotModel BuildElevationPlot(
            List<DssHydrographReader.HydrographRecord>? records = null)
        {
            var model = new PlotModel
            {
                TextColor = OxyColorPalette.Colors["TextAxis"],
                PlotAreaBorderColor = OxyColorPalette.Colors["DimGray"],
                PlotAreaBorderThickness = new OxyThickness(1),
                PlotMargins = new OxyThickness(38, 5, 5, 40),
                DefaultFontSize = 10,
            };

            var xAxis = MakeAxis(AxisPosition.Bottom, "TIME IN HOURS");
            xAxis.Minimum = 0;
            xAxis.MajorStep = 6;

            var yAxis = MakeAxis(AxisPosition.Left, "ELEVATION");
            //yAxis.Minimum = 0;

            model.Axes.Add(xAxis);
            model.Axes.Add(yAxis);

            var elevSeries = new LineSeries
            {
                Title = "ELEVATION",
                Color = OxyColors.DarkGreen,
                StrokeThickness = 2,
            };

            if (records is { Count: > 0 })
            {
                var startTime = records[0].Time;
                foreach (var r in records)
                    elevSeries.Points.Add(new DataPoint((r.Time - startTime).TotalHours, r.Value));
                xAxis.Maximum = (records.Last().Time - records[0].Time).TotalHours;
            }
            else
            {
                // placeholder curve
                var pts = new (double t, double e)[]
                {
                    (0,832),(6,833),(12,834),(18,836),(24,838),(30,840),
                    (36,843),(40,848),(44,854),(47,856.2),(48,856),(51,853),
                    (54,850),(60,845),(66,840),(72,836),(78,833),(84,831)
                };
                foreach (var (t, e) in pts)
                    elevSeries.Points.Add(new DataPoint(t, e));
            }

            model.Series.Add(elevSeries);
            model.Legends.Add(new Legend
            {
                LegendPosition = LegendPosition.TopRight,
                LegendPlacement = LegendPlacement.Inside,
                LegendFontSize = 9,
            });

            return model;
        }

        // ── Axis factory ─────────────────────────────────────────────────────

        private static LinearAxis MakeAxis(AxisPosition pos, string title = "") => new()
        {
            Position = pos,
            Title = title,
            AxislineStyle = LineStyle.Solid,
            AxislineColor = OxyColorPalette.Colors["DimGray"],
            AxislineThickness = 1,
            TickStyle = TickStyle.Outside,
            TicklineColor = OxyColorPalette.Colors["DimGray"],
            MajorGridlineStyle = LineStyle.Solid,
            MajorGridlineColor = OxyColorPalette.Colors["DimGray"],
            MinorGridlineStyle = LineStyle.Dot,
            MinorGridlineColor = OxyColorPalette.Colors["DimGray"],
            TextColor = OxyColorPalette.Colors["TextAxis"],
            TitleColor = OxyColorPalette.Colors["TextAxis"],
            FontSize = 10,
            TitleFontSize = 10,
        };
    }
}