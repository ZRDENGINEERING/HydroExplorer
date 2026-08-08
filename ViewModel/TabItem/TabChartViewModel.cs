using HydroExplorer.Core;
using HydroExplorer.Helpers;
using HydroExplorer.Themes;
using HydroExplorer.Utils;
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

        public double RainfallTotal { get => _rainfallTotal; set { _rainfallTotal = value; OnPropertyChanged(); } }
        public double LossTotal { get => _lossTotal; set { _lossTotal = value; OnPropertyChanged(); } }
        public double RainfallExcessTotal { get => _rainfallExcessTotal; set { _rainfallExcessTotal = value; OnPropertyChanged(); } }
        public double InitialLoss { get => _initialLoss; set { _initialLoss = value; OnPropertyChanged(); } }

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

        // Display labels for whichever runs are currently loaded into A/B —
        // bind these in XAML next to the plots so it's clear which run is
        // which series without needing to check the Hydrology tab.
        private string _runNameA = string.Empty;
        public string RunNameA { get => _runNameA; set { _runNameA = value; OnPropertyChanged(); } }

        private string _runNameB = string.Empty;
        public string RunNameB { get => _runNameB; set { _runNameB = value; OnPropertyChanged(); } }

        public ICommand GenerateReportCommand { get; }

        // ── Cached raw records — A and B load independently (either order,
        // either one alone), so each is cached separately and the combined
        // Hydrograph/Elevation plots are rebuilt from whichever is currently
        // known whenever either side updates. ─────────────────────────────
        private List<DssHydrographReader.HydrographRecord>? _flowRecordsA;
        private List<DssHydrographReader.HydrographRecord>? _flowCumRecordsA;
        private List<DssHydrographReader.HydrographRecord>? _elevRecordsA;

        private List<DssHydrographReader.HydrographRecord>? _flowRecordsB;
        private List<DssHydrographReader.HydrographRecord>? _flowCumRecordsB;
        private List<DssHydrographReader.HydrographRecord>? _elevRecordsB;

        // ── Constructor ──────────────────────────────────────────────────────

        public TabChartViewModel()
        {
            ElevationPlot = BuildElevationPlot();
            HydrographPlot = BuildHydrographPlot();
            HyetographPlot = BuildHyetographPlot(null);

            GenerateReportCommand = new RelayCommand(_ => GenerateReport());

            _ = LoadFromSettingsAsync();

            EventBus.DssRunSelected += (dssPath, runName) =>
            {
                LoadDssDataAsync(dssPath, runName);
            };

            EventBus.DssRunBSelected += (dssPath, runName) =>
            {
                LoadDssDataBAsync(dssPath, runName);
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
            System.Diagnostics.Debug.WriteLine($"LoadDssDataAsync (A): '{dssFile}' run='{runName}'");
            RunNameA = runName;

            var (precip, flow, flowCum, elev) = await ReadDssRunAsync(dssFile, runName);
            if (flow == null) return; // error already shown to the user inside ReadDssRunAsync

            _flowRecordsA = flow;
            _flowCumRecordsA = flowCum;
            _elevRecordsA = elev;

            if (precip is { Count: > 0 })
            {
                RainfallTotal = precip.Sum(r => r.Value);
                LossTotal = precip.Sum(r => r.Value * 0.05);
                RainfallExcessTotal = RainfallTotal - LossTotal;
                InitialLoss = 1.00;
                var plot = BuildHyetographPlot(precip);
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                    () => HyetographPlot = plot);
            }
            else
            {
                RainfallTotal = 0;
                LossTotal = 0;
                RainfallExcessTotal = 0;
                var emptyPlot = BuildHyetographPlot(null);
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                    () => HyetographPlot = emptyPlot);
            }

            RebuildHydrographPlot();
            RebuildElevationPlot();
        }

        private async void LoadDssDataBAsync(string dssFile, string runName)
        {
            System.Diagnostics.Debug.WriteLine($"LoadDssDataAsync (B): '{dssFile}' run='{runName}'");
            RunNameB = runName;

            var (_, flow, flowCum, elev) = await ReadDssRunAsync(dssFile, runName);
            if (flow == null) return;

            _flowRecordsB = flow;
            _flowCumRecordsB = flowCum;
            _elevRecordsB = elev;

            RebuildHydrographPlot();
            RebuildElevationPlot();
        }



        private async Task<(
            List<DssHyetographReader.HyetographRecord>? Precip,
            List<DssHydrographReader.HydrographRecord>? Flow,
            List<DssHydrographReader.HydrographRecord>? FlowCum,
            List<DssHydrographReader.HydrographRecord>? Elev)>
            ReadDssRunAsync(string dssFile, string runName)
        {
            try
            {
                if (!File.Exists(dssFile)) return (null, null, null, null);

                var sw = System.Diagnostics.Stopwatch.StartNew();

                var (precip, flow, flowBase, flowDirect, flowUG, flowCum, elev, storage) =
                    await Task.Run(() =>
                    {
                        Utils.DssGate.Enter();
                        try
                        {
                            using var dss = new Hec.Dss.DssReader(dssFile);
                            var catalog = dss.GetCatalog().ToList(); // fetch once, share across all 8 reads

                            var precip = DssHyetographReader.ReadPrecipInc(dss, runName, catalog);
                            var flow = DssHydrographReader.ReadByPartC(dss, "FLOW", runName, catalog);
                            var flowBase = DssHydrographReader.ReadByPartC(dss, "FLOW-BASE", runName, catalog);
                            var flowDirect = DssHydrographReader.ReadByPartC(dss, "FLOW-DIRECT", runName, catalog);
                            var flowUG = DssHydrographReader.ReadByPartC(dss, "FLOW-UNIT GRAPH", runName, catalog);
                            var flowCum = DssHydrographReader.ReadByPartC(dss, "FLOW-CUMULATIVE", runName, catalog);
                            var elev = DssHydrographReader.ReadByPartC(dss, "ELEVATION", runName, catalog);
                            var storage = DssHydrographReader.ReadByPartC(dss, "STORAGE", runName, catalog);

                            return (precip, flow, flowBase, flowDirect, flowUG, flowCum, elev, storage);
                        }
                        catch (Exception ex) when (Utils.DssErrorHelpers.IsUnsupportedVersion(ex))
                        {
                            System.Diagnostics.Debug.WriteLine($"ReadDssRunAsync: unsupported DSS version — {ex.Message}");
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                System.Windows.MessageBox.Show(
                                    Utils.DssErrorHelpers.UnsupportedVersionMessage,
                                    "Unsupported DSS File",
                                    System.Windows.MessageBoxButton.OK,
                                    System.Windows.MessageBoxImage.Warning));
                            return (null, null, null, null, null, null, null, null)!;
                        }
                        catch (IOException ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"ReadDssRunAsync: DSS file access error — {ex.Message}");
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                System.Windows.MessageBox.Show(
                                    "Can't read this DSS file right now.\n\n" + ex.Message,
                                    "DSS File Access Error",
                                    System.Windows.MessageBoxButton.OK,
                                    System.Windows.MessageBoxImage.Warning));
                            return (null, null, null, null, null, null, null, null)!;
                        }
                        finally
                        {
                            Utils.DssGate.Exit();
                        }
                    });

                sw.Stop();

                if (flow == null) return (null, null, null, null); // error path above already notified

                System.Diagnostics.Debug.WriteLine(
                    $"DSS complete in {sw.ElapsedMilliseconds}ms — " +
                    $"precip:{precip.Count} flow:{flow.Count} " +
                    $"flowBase:{flowBase.Count} flowDirect:{flowDirect.Count} " +
                    $"flowUG:{flowUG.Count} flowCum:{flowCum.Count} " +
                    $"elev:{elev.Count} storage:{storage.Count}");

                return (precip, flow, flowCum, elev);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ReadDssRunAsync error: {ex.Message}");
                return (null, null, null, null);
            }
        }

        private void RebuildHydrographPlot()
        {
            var plot = BuildHydrographPlot(
                _flowRecordsA is { Count: > 0 } ? _flowRecordsA : _flowCumRecordsA,
                _flowRecordsB is { Count: > 0 } ? _flowRecordsB : _flowCumRecordsB,
                RunNameA, RunNameB);

            System.Windows.Application.Current.Dispatcher.Invoke(() => HydrographPlot = plot);
        }

        private void RebuildElevationPlot()
        {
            bool hasAny = (_elevRecordsA?.Count ?? 0) > 0 || (_elevRecordsB?.Count ?? 0) > 0;

            var plot = BuildElevationPlot(_elevRecordsA, _elevRecordsB, RunNameA, RunNameB);

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                ElevationPlot = plot;
                ElevationDataAvailable = hasAny;
            });
        }


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
                    initialLoss: InitialLoss);

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

        /// <summary>
        /// Flow comparison — Run A solid, Run B dashed, both on the same
        /// hour-0-aligned time axis so they overlay even if the two runs'
        /// actual calendar start times differ.
        /// </summary>
        private static PlotModel BuildHydrographPlot(
            List<DssHydrographReader.HydrographRecord>? flowA = null,
            List<DssHydrographReader.HydrographRecord>? flowB = null,
            string labelA = "A", string labelB = "B")
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

            AddSeries(flowA, string.IsNullOrEmpty(labelA) ? "FLOW (A)" : $"FLOW ({labelA})", OxyColors.SteelBlue);
            AddSeries(flowB, string.IsNullOrEmpty(labelB) ? "FLOW (B)" : $"FLOW ({labelB})", OxyColors.OrangeRed, LineStyle.Dash);

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

        /// <summary>
        /// Elevation comparison — same A-solid/B-dashed convention as
        /// BuildHydrographPlot.
        /// </summary>
        private static PlotModel BuildElevationPlot(
            List<DssHydrographReader.HydrographRecord>? recordsA = null,
            List<DssHydrographReader.HydrographRecord>? recordsB = null,
            string labelA = "A", string labelB = "B")
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

            model.Axes.Add(xAxis);
            model.Axes.Add(yAxis);

            double maxHours = 0;

            void AddSeries(List<DssHydrographReader.HydrographRecord>? records,
                           string title, OxyColor color, LineStyle style = LineStyle.Solid)
            {
                if (records is not { Count: > 0 }) return;
                var series = new LineSeries { Title = title, Color = color, StrokeThickness = 2, LineStyle = style };
                var startTime = records[0].Time;
                foreach (var r in records)
                    series.Points.Add(new DataPoint((r.Time - startTime).TotalHours, r.Value));
                model.Series.Add(series);
                double elapsed = (records.Last().Time - records[0].Time).TotalHours;
                if (elapsed > maxHours) maxHours = elapsed;
            }

            AddSeries(recordsA, string.IsNullOrEmpty(labelA) ? "ELEVATION (A)" : $"ELEVATION ({labelA})", OxyColors.DarkGreen);
            AddSeries(recordsB, string.IsNullOrEmpty(labelB) ? "ELEVATION (B)" : $"ELEVATION ({labelB})", OxyColors.Purple, LineStyle.Dash);

            if (maxHours > 0)
                xAxis.Maximum = maxHours;

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