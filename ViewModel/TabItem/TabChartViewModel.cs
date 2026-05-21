using Hec.Dss;
using HydroExplorer.Core;
using HydroExplorer.Helpers;
using HydroExplorer.Themes;
using HydroExplorer.Utils;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using System.IO;
using System.Windows.Input;




namespace HydroExplorer.ViewModel.TabItem
{
    public class TabChartViewModel : TabViewModelBase
    {
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

        public ICommand GenerateReportCommand { get; }


        public TabChartViewModel()
        {
            var ep = BuildElevationPlot();
            var hp = BuildHydrographPlot();
            var hy = BuildHyetographPlot(null);

            ElevationPlot = ep;
            HydrographPlot = hp;
            HyetographPlot = hy;

            LoadDssDataAsync();

            GenerateReportCommand = new RelayCommand(_ => GenerateReport());

        }


        private void GenerateReport()
        {
            try
            {
                var path = @"C:\Temp\HydroReport.pdf";
                HydroReportGenerator.GenerateHydrographReport(
                    HydrographPlot!,
                    HyetographPlot!,
                    path,
                    projectName: "Boggy Creek — 100YR",
                    rainfallTotal: RainfallTotal,
                    lossTotal: LossTotal,
                    rainfallExcessTotal: RainfallExcessTotal,
                    initialLoss: InitialLoss,
                    infiltrationIndex: InfiltrationIndex);

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GenerateReport error: {ex.Message}");
            }
        }


        private async void LoadDssDataAsync()
        {
            try
            {
                var dssFile = @"C:\Temp\199805003 Bart Test\Boggy\Boggy.dss";
                if (!File.Exists(dssFile)) return;

                var sw = System.Diagnostics.Stopwatch.StartNew();

                var precipRecords = await Task.Run(() => DssHyetographReader.ReadPrecipInc(dssFile, "BOG_100YR"));
                var flowRecords = await Task.Run(() => DssHyetographReader.ReadByPartB(dssFile, "FLOW", "BOG_100YR"));
                var elevRecords = await Task.Run(() => DssHyetographReader.ReadByPartB(dssFile, "ELEVATION", "BOG_100YR"));
                var flowBaseRecords = await Task.Run(() => DssHyetographReader.ReadByPartB(dssFile, "FLOW-BASE", "BOG_100YR"));
                var flowDirectRecords = await Task.Run(() => DssHyetographReader.ReadByPartB(dssFile, "FLOW-DIRECT", "BOG_100YR"));
                var flowUGRecords = await Task.Run(() => DssHyetographReader.ReadByPartB(dssFile, "FLOW-UNIT GRAPH", "BOG_100YR"));

                sw.Stop();
                System.Diagnostics.Debug.WriteLine($"DSS complete in {sw.ElapsedMilliseconds}ms");

                if (precipRecords.Count > 0)
                {
                    RainfallTotal = precipRecords.Sum(r => r.Value);
                    LossTotal = precipRecords.Sum(r => r.Value * 0.05);
                    RainfallExcessTotal = RainfallTotal - LossTotal;
                    InitialLoss = 1.00;
                    InfiltrationIndex = 0.10;
                    var plot = BuildHyetographPlot(precipRecords);
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        HyetographPlot = plot);
                }

                if (flowRecords.Count > 0 || elevRecords.Count > 0)
                {
                    var plot = BuildHydrographPlot(flowRecords, flowBaseRecords, flowDirectRecords, flowUGRecords);
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        HydrographPlot = plot);
                }

                if (elevRecords.Count > 0)
                {
                    var plot = BuildElevationPlot(elevRecords);
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        ElevationPlot = plot);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadDssDataAsync error: {ex.Message}");
            }
        }





        private async void LoadHyetographAsync()
        {
            try
            {
                var dssFile = @"C:\Temp\199805003 Bart Test\Boggy\Boggy.dss";

                if (!File.Exists(dssFile))
                {
                    System.Diagnostics.Debug.WriteLine($"DSS file not found: {dssFile}");
                    return;
                }

                var sw = System.Diagnostics.Stopwatch.StartNew();
                System.Diagnostics.Debug.WriteLine("DSS read starting...");

                var records = await Task.Run(() =>
                    DssHyetographReader.ReadPrecipInc(dssFile, preferredRun: "BOG_100YR"));

                sw.Stop();
                System.Diagnostics.Debug.WriteLine($"DSS read complete in {sw.ElapsedMilliseconds}ms — {records.Count} records.");

                if (records.Count > 0)
                {
                    RainfallTotal = records.Sum(r => r.Value);
                    LossTotal = records.Sum(r => r.Value * 0.05);
                    RainfallExcessTotal = RainfallTotal - LossTotal;
                    InitialLoss = 1.00;
                    InfiltrationIndex = 0.10;

                    var plot = BuildHyetographPlot(records);

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        HyetographPlot = plot;
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadHyetographAsync error: {ex.Message}");
            }
        }


        public PlotModel? HyetographPlot
        {
            get => _hyetographPlot;
            set { _hyetographPlot?.InvalidatePlot(false); _hyetographPlot = value; OnPropertyChanged(); }
        }


        private static PlotModel BuildHyetographPlot(List<DssHyetographReader.HyetographRecord>? records)
        {
            var model = new PlotModel
            {
                TextColor = OxyColorPalette.Colors["TextAxis"],
                PlotAreaBorderColor = OxyColorPalette.Colors["DimGray"],
                PlotAreaBorderThickness = new OxyThickness(1),
                PlotMargins = new OxyThickness(60, 10, 10, 40),
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





        // ── Axis factories ───────────────────────────────────────────────────────

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

        private static CategoryAxis MakeCategoryAxis(AxisPosition pos, string title = "") => new()
        {
            Position = pos,
            Title = title,
            GapWidth = 0.1,
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




        private string _header = "Chart";
        public override string Header
        {
            get => _header;
            set { _header = value; OnPropertyChanged(nameof(Header)); }
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
                {
                    old?.InvalidatePlot(false);
                    OnPropertyChanged();
                });
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
                {
                    old?.InvalidatePlot(false);
                    OnPropertyChanged();
                });
            }
        }


        private static PlotModel BuildElevationPlot(List<DssHyetographReader.HyetographRecord>? records = null)
        {
            var model = new PlotModel
            {
                TextColor = OxyColorPalette.Colors["TextAxis"],
                PlotAreaBorderColor = OxyColorPalette.Colors["DimGray"],
                PlotAreaBorderThickness = new OxyThickness(1),
                PlotMargins = new OxyThickness(60, 10, 10, 40),
                DefaultFontSize = 10,
            };

            var xAxis = MakeAxis(AxisPosition.Bottom, "TIME IN HOURS");
            xAxis.Minimum = 0;
            xAxis.MajorStep = 6;

            var yAxis = MakeAxis(AxisPosition.Left, "ELEVATION (FT NGVD29)");
            yAxis.Minimum = 0;

            model.Axes.Add(xAxis);
            model.Axes.Add(yAxis);

            var elevSeries = new LineSeries
            {
                Title = "ELEVATION",
                Color = OxyColors.SteelBlue,
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
                // dummy fallback
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

        private static PlotModel BuildHydrographPlot(
            List<DssHyetographReader.HyetographRecord>? flow = null,
            List<DssHyetographReader.HyetographRecord>? flowBase = null,
            List<DssHyetographReader.HyetographRecord>? flowDirect = null,
            List<DssHyetographReader.HyetographRecord>? flowUG = null)
        {
            var model = new PlotModel
            {
                TextColor = OxyColorPalette.Colors["TextAxis"],
                PlotAreaBorderColor = OxyColorPalette.Colors["DimGray"],
                PlotAreaBorderThickness = new OxyThickness(1),
                PlotMargins = new OxyThickness(60, 10, 10, 40),
                DefaultFontSize = 10,
            };

            var xAxis = MakeAxis(AxisPosition.Bottom, "TIME IN HOURS");
            xAxis.Minimum = 0;
            xAxis.MajorStep = 6;

            var yAxis = MakeAxis(AxisPosition.Left, "DISCHARGE (CFS)");
            yAxis.Minimum = 0;

            model.Axes.Add(xAxis);
            model.Axes.Add(yAxis);

            void AddSeries(List<DssHyetographReader.HyetographRecord>? records, string title, OxyColor color, LineStyle style = LineStyle.Solid)
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

                if (records.Count > 0)
                    xAxis.Maximum = Math.Max(xAxis.Maximum, (records.Last().Time - records[0].Time).TotalHours);
            }

            AddSeries(flow, "FLOW", OxyColors.SteelBlue);
            AddSeries(flowBase, "FLOW-BASE", OxyColors.DarkGreen, LineStyle.Dash);
            AddSeries(flowDirect, "FLOW-DIRECT", OxyColors.DarkRed, LineStyle.Dash);
            AddSeries(flowUG, "FLOW-UNIT GRAPH", OxyColors.Orange, LineStyle.Dot);

            model.Legends.Add(new Legend
            {
                LegendPosition = LegendPosition.TopRight,
                LegendPlacement = LegendPlacement.Inside,
                LegendFontSize = 9,
            });

            return model;
        }









    }
}
