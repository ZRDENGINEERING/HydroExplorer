using BruTile.Predefined;
using HydroExplorer.Helpers;
using HydroExplorer.Utils;
using HydroExplorer.ViewModel;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Nts.Providers.Shapefile;
using Mapsui.Nts.Widgets;
using Mapsui.Projections;
using Mapsui.Providers;
using Mapsui.Providers.Wms;
using Mapsui.Styles;
using Mapsui.Tiling.Layers;
using Mapsui.UI.Wpf;
using Mapsui.Widgets.InfoWidgets;
using Microsoft.Extensions.DependencyInjection;
using PureHDF;
using PureHDF.VOL.Native;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;


namespace HydroExplorer.View
{
    public partial class MapView : UserControl
    {
        private readonly MapStateService _mapState;
        private readonly IUserSettingsRepo _settingsRepo;
        public readonly EditingWidget? _editingWidget;
        private readonly MapControl _mapControl;
        private readonly Map _map;

        private string? _PathRiver = string.Empty;
        private string? _pathXS = string.Empty;
        private string? _pathHdfA = string.Empty;
        private string? _pathHdfB = string.Empty;
        private string? _pathBNDY = string.Empty;
        private string? _pathHMS = string.Empty;
        private string? _pathSubBasins = string.Empty;
        private string? _currentProjPath;

        private GageResult? _currentGage;

        private static readonly Color _colorLblBackGround = new(236, 210, 1, 150);
        private static readonly Color _lblBackGroundColor = new(236, 210, 1, 150);
        private static readonly Color _vecCLColor = new(70, 0, 0, 255);
        private static readonly Color _vecBNDYColor = new(70, 138, 138, 255);
        private static readonly Color _vecBNDYFill = new(128, 128, 128, 0);
        private static readonly Color _txtLBLColor = new(0, 0, 0, 155);

        private static readonly string _tileCachePath = Path.Combine(
            Path.GetTempPath(), "HydroExplorer", "TileCache");

        private readonly SelectionViewModel _selectionVm;
        private readonly WritableLayer _highlightLayer;
        private CancellationTokenSource? _resetMapCts;


        private string? ResolveHdfPath() =>
            !string.IsNullOrEmpty(_pathHdfB) && File.Exists(_pathHdfB) ? _pathHdfB :
            !string.IsNullOrEmpty(_pathHdfA) && File.Exists(_pathHdfA) ? _pathHdfA :
            null;


        public MapView()
        {
            InitializeComponent();

            _mapState = App.ServiceProvider.GetRequiredService<MapStateService>();
            _settingsRepo = App.ServiceProvider.GetRequiredService<IUserSettingsRepo>();

            _mapControl = new Mapsui.UI.Wpf.MapControl
            {
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(71, 71, 73)),
                Opacity = 0
            };

            _map = new Map { CRS = "EPSG:3857" };
            LoggingWidget.ShowLoggingInMap = Mapsui.Widgets.ActiveMode.No;

            var (cx, cy) = SphericalMercator.FromLonLat(-99.0, 31.0);
            _map.Navigator.CenterOnAndZoomTo(new MPoint(cx, cy), 3000);


            _highlightLayer = new WritableLayer
            {
                Name = "Highlight",
                Style = new VectorStyle { Outline = new Pen(Color.LightYellow, 2), 
                    Opacity=0.1f,
                    Fill = null }
            };
            _map.Layers.Add(_highlightLayer);

            _selectionVm = App.ServiceProvider.GetRequiredService<SelectionViewModel>();
            _selectionVm.PropertyChanged += OnSelectionChanged;

            _mapControl.Map = _map;
            _map.Widgets.Clear();
            Content = _mapControl;

            Loaded += async (s, e) =>
            {
                if (_mapState.CurrentPaths == null)
                    await ResetMapView();
            };

            EventBus.ProjPathChanged += async path =>
            {
                await Dispatcher.InvokeAsync(async () =>
                {
                    try { await ResetMapView(path); }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"ResetMap error: {ex.Message}"); }
                });
            };

            EventBus.GageDataReady += gage =>
            {
                Dispatcher.Invoke(() =>
                {
                    _currentGage = gage;
                    AddLayerGage();
                    _mapControl.Refresh();
                });
            };

            Unloaded += (s, e) =>
            {
                _mapState.PathsReady -= OnPathsReady;
                EventBus.MapOverViewReady -= OnMapOverViewReady;
            };

            // Subscribe and replay LAST — after everything is initialized
            _mapState.PathsReady += OnPathsReady;
            if (_mapState.CurrentPaths != null)
            {
                //System.Diagnostics.Debug.WriteLine($"MapView constructor: replaying CurrentPaths='{_mapState.CurrentPaths.ProjPath}'");
                OnPathsReady(_mapState.CurrentPaths);
            }
        }



        private void OnPathsReady(ProjectPaths paths)
        {
            //System.Diagnostics.Debug.WriteLine($"MapView.OnPathsReady FIRED");

            if (paths.ProjPath == _currentProjPath)
            {
                //System.Diagnostics.Debug.WriteLine("MapView.OnPathsReady: same project, skipping reset.");
                return;
            }

            _currentProjPath = paths.ProjPath;


            Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                //System.Diagnostics.Debug.WriteLine($"MapView.OnPathsReady dispatcher entered");
                try
                {
                    if (!IsLoaded)
                    {
                        //System.Diagnostics.Debug.WriteLine("MapView.OnPathsReady: waiting for Loaded...");
                        var tcs = new TaskCompletionSource<bool>();
                        void OnLoaded(object s, RoutedEventArgs e) { tcs.TrySetResult(true); }
                        Loaded += OnLoaded;
                        await tcs.Task;
                        Loaded -= OnLoaded;
                        //System.Diagnostics.Debug.WriteLine("MapView.OnPathsReady: Loaded complete.");
                    }

                    _pathXS = paths.PathXS;
                    _PathRiver = paths.PathRiver;
                    _pathBNDY = paths.PathBNDY;
                    _pathHdfA = paths.PathHdfA;
                    _pathHdfB = paths.PathHdfB;
                    _pathHMS = paths.PathHMS;
                    _pathSubBasins = paths.PathSubBasins;

                    await ResetMapView(paths.ProjPath);
                    await InitView();

                    _ = ExportAndReloadCLAsync(_resetMapCts?.Token ?? CancellationToken.None);
                }
                catch (OperationCanceledException)
                {
                    System.Diagnostics.Debug.WriteLine("MapView.OnPathsReady: cancelled.");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"MapView.OnPathsReady error: {ex.Message}");
                }
            });
        }


        public async Task ResetMapView(string projPath = "")
        {
            _resetMapCts?.Cancel();
            _resetMapCts?.Dispose();
            _resetMapCts = new CancellationTokenSource();
            var token = _resetMapCts.Token;

            await Dispatcher.InvokeAsync(() => _mapControl.Opacity = 0);
            await Task.Delay(100, token);

            _map.Layers.Clear();
            _map.Layers.Add(CreateDarkBackdropLayer());

            var tileSource = KnownTileSources.Create(
                KnownTileSource.BingHybrid,
                apiKey: null,
                persistentCache: new BruTile.Cache.FileCache(_tileCachePath, "png"));

            var lyr_bing = new TileLayer(tileSource) { Opacity = 0 }; // start invisible
            _map.Layers.Add(lyr_bing);

            try
            {
                await AddLayerShpTXCnty();
                await AddLayerShpBndy();
                await ExportShpXS();
                await ExportShpRiver();
                await AddLayerShpZRD();

                var plotVm = App.ServiceProvider.GetRequiredService<PlotViewModel>();
                if (plotVm.WselData != null)
                    await AddLayerShpXS(plotVm.WselData);
                else
                    System.Diagnostics.Debug.WriteLine("ResetMapView: WselData null — skipping XS layer.");

                await AddLayerShpRiver();
                AddLayerGage();
                _map.Layers.Add(_highlightLayer);

                await InitView();
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ResetMap error: {ex.Message}");
                return;
            }

            // Fade tile layer in over dark backdrop, then fade control in
            await Dispatcher.InvokeAsync(() => _mapControl.Refresh());
            await WaitForTilesAsync(token, timeoutMs: 800);
            await FadeInTileLayerAsync(token);

            await Dispatcher.InvokeAsync(() =>
            {
                var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
                anim.Completed += (_, _) => EventBus.PublishMapOverViewReady();
                _mapControl.BeginAnimation(OpacityProperty, anim);
            });

            _ = ExportAndReloadCLAsync(_resetMapCts.Token);
        }


        private async Task InitView()
        {
            string? centerPath = Path.Exists(_pathBNDY) ? _pathBNDY
                               : Path.Exists(_pathXS) ? _pathXS
                               : null;

            //System.Diagnostics.Debug.WriteLine(
            //    $"InitView: centerPath='{centerPath}' (_pathBNDY='{_pathBNDY}' exists={Path.Exists(_pathBNDY)}, _pathXS='{_pathXS}' exists={Path.Exists(_pathXS)})");


            await WaitForStableSizeAsync();

            await Dispatcher.InvokeAsync(() =>
            {
                if (centerPath == null)
                {
                    // No geometry — return to default Texas view
                    var (cx, cy) = SphericalMercator.FromLonLat(-99.0, 31.0);
                    _map.Navigator.CenterOnAndZoomTo(new MPoint(cx, cy), 3000);
                    _mapControl.Refresh();
                    return;
                }

                var shapeFileProvider = new ShapeFile(centerPath, true);
                if (shapeFileProvider.GetExtent() is not MRect extent) return;

                //System.Diagnostics.Debug.WriteLine(
                //    $"InitView: extent from '{centerPath}' = MinX={extent.MinX} MaxX={extent.MaxX} MinY={extent.MinY} MaxY={extent.MaxY}");


                var (x, y) = SphericalMercator.FromLonLat(
                                    (extent.MaxX + extent.MinX) / 2,
                                    (extent.MaxY + extent.MinY) / 2);
                var (minX, _) = SphericalMercator.FromLonLat(extent.MinX, extent.MinY);
                var (maxX, _) = SphericalMercator.FromLonLat(extent.MaxX, extent.MaxY);
                var (_, minY) = SphericalMercator.FromLonLat(extent.MinX, extent.MinY);
                var (_, maxY) = SphericalMercator.FromLonLat(extent.MaxX, extent.MaxY);

                double extentW = maxX - minX;
                double extentH = maxY - minY;
                double controlW = _mapControl.ActualWidth;
                double controlH = _mapControl.ActualHeight;

                if (extentW <= 0 || extentH <= 0 || controlW <= 0 || controlH <= 0) return;

                double resolution = Math.Max(extentW / controlW, extentH / controlH) * 1.5;
                _map.Navigator.CenterOnAndZoomTo(new MPoint(x, y), resolution);
                _mapControl.Refresh();
            });
        }



        private Task WaitForStableSizeAsync()
        {
            if (_mapControl.ActualWidth > 0 && _mapControl.ActualHeight > 0)
                return Task.CompletedTask;

            var tcs = new TaskCompletionSource<bool>();

            void OnSizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
            {
                if (_mapControl.ActualWidth > 0 && _mapControl.ActualHeight > 0)
                {
                    _mapControl.SizeChanged -= OnSizeChanged;
                    tcs.TrySetResult(true);
                }
            }

            _mapControl.SizeChanged += OnSizeChanged;
            return tcs.Task;
        }


        private async Task AddLayerShpBndy()
        {
            if (!Path.Exists(_pathBNDY)) return;

            var shapeFileProvider = new ShapeFile(_pathBNDY) { CRS = "EPSG:4326" };
            var dataSource = new ProjectingProvider(shapeFileProvider) { CRS = "EPSG:3857" };

            var shapefileLayer = new Layer("BNDY")
            {
                Name = "BNDY",
                DataSource = dataSource,
                Tag = new OverViewLayerData { IsMapInfoLayer = true },
                Style = new StyleCollection
                {
                    Styles =
                    {
                        new VectorStyle
                        {
                            Fill    = new Brush(_vecBNDYFill),
                            Outline = new Pen(_vecBNDYColor, 7),
                        },
                        new VectorStyle
                        {
                            Fill    = new Brush(_vecBNDYFill),
                            Outline = new Pen(Color.Black, 1),
                        },
                    }
                }
            };
            _map.Layers.Add(new RasterizingTileLayer(shapefileLayer));
        }


        private async Task AddLayerShpXS(List<WSELTableOxy> wselData)
        {
            if (!Path.Exists(_pathXS)) return;

            var xsLayer = new WritableLayer
            {
                Name = "XS",
                Tag = new OverViewLayerData { IsMapInfoLayer = true }
            };

            using var reader = new NetTopologySuite.IO.ShapefileDataReader(
                _pathXS, NetTopologySuite.Geometries.GeometryFactory.Default);

            var header = reader.DbaseHeader;
            int staIdx = -1;
            for (int i = 0; i < header.Fields.Length; i++)
            {
                if (header.Fields[i].Name.Contains("RS", StringComparison.OrdinalIgnoreCase))
                { staIdx = i + 1; break; }
            }

            var factory = NetTopologySuite.Geometries.GeometryFactory.Default;

            // O(1) lookup instead of FirstOrDefault on every feature
            var wselIndex = new Dictionary<string, WSELTableOxy>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in wselData)
            {
                string key = w.RiverSta?.Trim() ?? string.Empty;
                wselIndex.TryAdd(key, w); // silently skip duplicates
            }

            while (reader.Read())
            {
                var geom = reader.Geometry;
                var rs = staIdx >= 0 ? reader.GetString(staIdx)?.Trim() : null;

                var coords = geom.Coordinates
                    .Select(c =>
                    {
                        var (px, py) = SphericalMercator.FromLonLat(c.X, c.Y);
                        return new NetTopologySuite.Geometries.Coordinate(px, py);
                    })
                    .ToArray();

                var projectedGeom = factory.CreateLineString(coords);
                var feature = new GeometryFeature { Geometry = projectedGeom };
                feature["RS"] = rs;

                var lineColor = wselIndex.TryGetValue(rs ?? string.Empty, out var match) && match.DELTA > 0
                    ? new Color(220, 50, 50, 255)
                    : new Color(50, 205, 50, 255);

                feature.Styles.Add(new VectorStyle
                {
                    Line = new Pen(lineColor, 1),
                    Outline = new Pen(lineColor, 1),
                    Fill = null,
                    Opacity = 0.85f
                });

                feature.Styles.Add(new LabelStyle
                {
                    LabelColumn = "RS",
                    ForeColor = Color.White,
                    BackColor = new Brush(new Color(0, 0, 0, 150)),
                    CornerRounding = 2,
                    Font = new Font { FontFamily = "Arial", Size = 6, Bold = false },
                    HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Left,
                    VerticalAlignment = LabelStyle.VerticalAlignmentEnum.Center,
                    MaxVisible = 5,
                    Offset = new Offset { X = 0, Y = 0 }
                });

                xsLayer.Add(feature);
            }
            _map.Layers.Add(xsLayer);
        }


        private async Task AddLayerShpRiver()
        {
            if (!Path.Exists(_PathRiver)) return;

            var shapeFileProvider = new ShapeFile(_PathRiver) { CRS = "EPSG:4326" };
            var dataSource = new ProjectingProvider(shapeFileProvider) { CRS = "EPSG:3857" };

            var shapefileLayer = new Layer("CL")
            {
                Name = "CL",
                DataSource = dataSource,
                Tag = new OverViewLayerData { IsMapInfoLayer = true },
                Style = new StyleCollection
                {
                    Styles =
                    {
                        new VectorStyle
                        {
                            Line = new Pen { Color = Color.Blue, Width = 1.5f }
                        }
                    }
                }
            };
            _map.Layers.Add(new RasterizingTileLayer(shapefileLayer));
        }


        private Task AddLayerShpTXCnty()
        {
            string shapefilePathCnty = "Z:\\10 DEV\\hydroExplorer\\SHP\\TX_CNTY.shp";
            if (!Path.Exists(shapefilePathCnty)) return Task.CompletedTask;

            var shapeFileProvider = new ShapeFile(shapefilePathCnty) { CRS = "EPSG:4326" };
            var dataSource = new ProjectingProvider(shapeFileProvider) { CRS = "EPSG:3857" };

            var shapefileLayer = new Layer("CNTY")
            {
                Name = "CNTY",
                DataSource = dataSource,
                Tag = new OverViewLayerData { IsMapInfoLayer = true },
                Style = new StyleCollection
                {
                    Styles =
                    {
                        new VectorStyle
                        {
                            Fill    = new Brush(Color.Transparent),
                            Outline = new Pen(Color.Black, 2),
                            Opacity = 0.3f
                        },
                        new LabelStyle
                        {
                            Enabled             = true,
                            LabelColumn         = "NAME",
                            BackColor           = new Brush(Color.Transparent),
                            ForeColor           = new Color(_txtLBLColor),
                            Font                = new Font { FontFamily = "Eras", Size = 14, Bold = true },
                            HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Center,
                            VerticalAlignment   = LabelStyle.VerticalAlignmentEnum.Center,
                            MaxVisible          = 1000,
                        }
                    }
                }
            };
            _map.Layers.Add(new RasterizingTileLayer(shapefileLayer));
            return Task.CompletedTask;
        }

        private void AddLayerGage()
        {
            var existing = _map.Layers.FirstOrDefault(l => l.Name == "USGS_GAGE");
            if (existing != null)
                _map.Layers.Remove(existing);

            if (_currentGage is null) return;

            var (x, y) = SphericalMercator.FromLonLat(_currentGage.Lon, _currentGage.Lat);
            var point = new NetTopologySuite.Geometries.Point(x, y);

            var feature = new GeometryFeature { Geometry = point };
            feature["label"] = _currentGage.SiteName;

            var gageLayer = new MemoryLayer("USGS_GAGE")
            {
                Name = "USGS_GAGE",
                Features = new[] { feature },
                Style = new StyleCollection
                {
                    Styles =
            {
                new SymbolStyle
                {
                    SymbolType = SymbolType.Triangle,
                    Fill = new Brush(Color.FromArgb(255, 30, 144, 255)),
                    Outline = new Pen(Color.Black, 1),
                    SymbolScale = 0.4
                }
            }
                }
            };

            _map.Layers.Add(gageLayer);
        }


        private async Task ExportShpXS()
        {
            try
            {
                string? hdfPath = ResolveHdfPath();
                if (string.IsNullOrEmpty(hdfPath)) return;
                if (string.IsNullOrEmpty(_pathXS)) return;
                if (File.Exists(_pathXS)) return;

                //System.Diagnostics.Debug.WriteLine($"XS FILE DOES NOT EXIST — CREATING @ {_pathXS}");

                string projDir = Path.GetDirectoryName(hdfPath) ?? string.Empty;

                bool exported = await ExporterXS.ExportXSToShp(
                    projPath: projDir,
                    hdfPath: hdfPath,
                    outputShpPath: _pathXS);

                if (exported) return;

                // No .prj folder found for this HEC-RAS project — resolve (cached) or
                // guess+confirm a Texas State Plane zone, same path the river export
                // shares, so the user is only ever prompted once per project.
                int? epsg = await ResolveSourceEpsgAsync(
                    hdfPath,
                    "/Geometry/Cross Sections/Polyline Points",
                    "cross sections");

                if (epsg is not > 0) return;

                await ExporterXS.ExportXSToShp(
                    projPath: projDir,
                    hdfPath: hdfPath,
                    outputShpPath: _pathXS,
                    sourceEpsgOverride: epsg);
            }
            catch (OperationCanceledException) { System.Diagnostics.Debug.WriteLine("ExportXS cancelled."); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"ExportXS error: {ex.Message}"); }
        }

        /// <summary>
        /// Reads the raw (unprojected) extent of a polyline-points dataset directly
        /// from the HDF, for CRS zone-guessing when no .prj sidecar/folder exists to
        /// tell us what those coordinates are in. datasetPath is e.g.
        /// "/Geometry/Cross Sections/Polyline Points" or
        /// "/Geometry/River Centerlines/Polyline Points".
        /// </summary>
        private static NetTopologySuite.Geometries.Envelope? TryReadHdfPolylineExtent(string hdfPath, string datasetPath)
        {
            try
            {
                using var file = HecRasHdfReader.OpenHdf(hdfPath);
                double[] allPoints = file.Dataset(datasetPath).Read<double[]>();

                if (allPoints.Length < 2) return null;

                double minX = double.MaxValue, maxX = double.MinValue;
                double minY = double.MaxValue, maxY = double.MinValue;

                for (int i = 0; i < allPoints.Length; i += 2)
                {
                    double x = allPoints[i];
                    double y = allPoints[i + 1];
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }

                return new NetTopologySuite.Geometries.Envelope(minX, maxX, minY, maxY);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TryReadHdfPolylineExtent('{datasetPath}') failed — {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Resolves the source EPSG to use when a HEC-RAS project has no .prj sidecar
        /// folder — checked once per project and shared across exporters (XS, river)
        /// so the zone is guessed and confirmed by the user only once, then cached in
        /// ProjectSettings.SourceEpsg for the lifetime of the project.
        /// Returns null/non-positive if no project key is resolvable, no zone can be
        /// guessed from the HDF's raw coordinates, or the user declines the prompt.
        /// </summary>
        private async Task<int?> ResolveSourceEpsgAsync(string hdfPath, string datasetPath, string featureLabel)
        {
            string projKey = PathHelpers.NormalizeProjKey(_currentProjPath ?? string.Empty);
            var settings = await _settingsRepo.GetSettings();

            if (!string.IsNullOrEmpty(projKey) &&
                settings.Projects.TryGetValue(projKey, out var existing) &&
                existing.SourceEpsg is > 0)
            {
                //System.Diagnostics.Debug.WriteLine(
                //    $"ResolveSourceEpsgAsync: using cached EPSG:{existing.SourceEpsg} for project '{projKey}'.");
                return existing.SourceEpsg;
            }

            var rawExtent = TryReadHdfPolylineExtent(hdfPath, datasetPath);
            int guessedEpsg = rawExtent != null ? GISUtil.GuessTexasStatePlaneZone(rawExtent) : -1;

            if (guessedEpsg <= 0)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"ResolveSourceEpsgAsync: no .prj folder and could not guess a Texas State Plane zone — skipping {featureLabel}.");
                return null;
            }

            // Silenced: previously prompted the user to confirm the guessed zone via
            // MessageBox. Now auto-accepts the guess and proceeds — kept here,
            // commented, in case we want to re-enable confirmation later.
            //
            // var confirm = await Dispatcher.InvokeAsync(() => MessageBox.Show(
            //     $"This HEC-RAS project has no projection (.prj) file. Based on its coordinates, " +
            //     $"it's likely EPSG:{guessedEpsg}. Use this projection for {featureLabel}?",
            //     "Unknown Projection — Confirm Guess",
            //     MessageBoxButton.YesNo, MessageBoxImage.Question));
            //
            // if (confirm != MessageBoxResult.Yes) return null;

            //System.Diagnostics.Debug.WriteLine(
            //    $"ResolveSourceEpsgAsync: no .prj folder for {featureLabel} — auto-accepting guessed EPSG:{guessedEpsg} without confirmation.");

            if (!string.IsNullOrEmpty(projKey))
            {
                if (!settings.Projects.TryGetValue(projKey, out var proj))
                    proj = settings.Projects[projKey] = new ProjectSettings { ProjPath = projKey };

                proj.SourceEpsg = guessedEpsg;
                await _settingsRepo.SaveSettings(settings);
                System.Diagnostics.Debug.WriteLine(
                    $"ResolveSourceEpsgAsync: cached EPSG:{guessedEpsg} for project '{projKey}'.");
            }

            return guessedEpsg;
        }


        private async Task ExportShpRiver()
        {
            try
            {
                string? hdfPath = ResolveHdfPath();
                if (string.IsNullOrEmpty(hdfPath)) return;
                if (string.IsNullOrEmpty(_PathRiver)) return;
                if (File.Exists(_PathRiver)) return;

                //System.Diagnostics.Debug.WriteLine($"CL FILE DOES NOT EXIST — CREATING @ {_PathRiver}");

                string projDir = Path.GetDirectoryName(hdfPath) ?? string.Empty;

                bool exported = await ExporterRiver.ExportRiverToShp(
                    projPath: projDir,
                    hdfPath: hdfPath,
                    outputShpPath: _PathRiver);

                if (exported) return;

                // No .prj folder found — reuse the EPSG already guessed/confirmed
                // during XS export for this project (if any), or guess+confirm now
                // and cache it, so a later XS export (if it runs after) reuses it too.
                int? epsg = await ResolveSourceEpsgAsync(
                    hdfPath,
                    "/Geometry/River Centerlines/Polyline Points",
                    "river centerlines");

                if (epsg is not > 0) return;

                await ExporterRiver.ExportRiverToShp(
                    projPath: projDir,
                    hdfPath: hdfPath,
                    outputShpPath: _PathRiver,
                    sourceEpsgOverride: epsg);
            }
            catch (OperationCanceledException) { System.Diagnostics.Debug.WriteLine("ExportCL cancelled."); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"ExportCL error: {ex.Message}"); }
        }


        private async Task ExportAndReloadCLAsync(CancellationToken token)
        {
            bool existed = File.Exists(_PathRiver);
            await ExportShpRiver();

            if (!existed && File.Exists(_PathRiver))
            {
                await Dispatcher.InvokeAsync(async () =>
                {
                    var old = _map.Layers.FindLayer("CL").FirstOrDefault();
                    if (old != null) _map.Layers.Remove(old);

                    await AddLayerShpRiver();
                    _mapControl.Refresh();
                });
            }
        }


        private void OnMapOverViewReady()
        {
            Dispatcher.InvokeAsync(async () =>
            {
                var cts = _resetMapCts;
                if (cts == null || cts.IsCancellationRequested) return;

                try { await ExportAndReloadCLAsync(cts.Token); }
                catch (OperationCanceledException) { }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Deferred CL export error: {ex.Message}"); }
            });
        }


        private void OnMapTapped(object? sender, MapEventArgs e)
        {
            var xsLayer = _map.Layers.FindLayer("XS").FirstOrDefault();
            if (xsLayer == null) return;

            var mapInfo = _mapControl.GetMapInfo(e.ScreenPosition, [xsLayer]);
            if (mapInfo?.Feature is not GeometryFeature feature) return;

            var rs = feature["RS"]?.ToString();
            if (string.IsNullOrEmpty(rs)) return;

            _selectionVm.SelectedRiverSta = rs;
        }


        private void OnSelectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SelectionViewModel.SelectedRiverSta))
                ZoomToStation(_selectionVm.SelectedRiverSta, _selectionVm.SelectedDelta);
        }


        private void ZoomToStation(string? riverSta, double delta)
        {
            if (string.IsNullOrEmpty(riverSta)) return;
            if (!Path.Exists(_pathXS)) return;

            try
            {
                using var reader = new NetTopologySuite.IO.ShapefileDataReader(
                    _pathXS, NetTopologySuite.Geometries.GeometryFactory.Default);

                var header = reader.DbaseHeader;
                int staIdx = -1;
                for (int i = 0; i < header.Fields.Length; i++)
                {
                    if (header.Fields[i].Name.Contains("RS", StringComparison.OrdinalIgnoreCase))
                    { staIdx = i + 1; break; }
                }

                if (staIdx == -1) return;

                while (reader.Read())
                {
                    var val = reader.GetString(staIdx)?.Trim();
                    if (val != riverSta?.Trim()) continue;

                    try
                    {
                        var geom = reader.Geometry;
                        var centroid = geom.Centroid;
                        var (x, y) = SphericalMercator.FromLonLat(centroid.X, centroid.Y);
                        var factory = NetTopologySuite.Geometries.GeometryFactory.Default;

                        var coords = geom.Coordinates
                            .Select(c =>
                            {
                                var (px, py) = SphericalMercator.FromLonLat(c.X, c.Y);
                                return new NetTopologySuite.Geometries.Coordinate(px, py);
                            })
                            .ToArray();

                        var projectedGeom = factory.CreateLineString(coords);
                        var feature = new GeometryFeature { Geometry = projectedGeom };
                        var highlightColor = new Color(255, 255, 0, 100);

                        _mapControl.Dispatcher.Invoke(() =>
                        {
                            _highlightLayer.Style = new VectorStyle
                            {
                                Line = new Pen(highlightColor, 3),
                                Outline = new Pen(highlightColor, 3),
                                Fill = null
                            };

                            _highlightLayer.Clear();
                            _highlightLayer.Add(feature);
                            _highlightLayer.DataHasChanged();
                            _map.Navigator.CenterOn(x, y);
                            _map.Navigator.ZoomTo(1);
                            _mapControl.Refresh();
                        });
                    }
                    catch (Exception innerEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"ZoomToStation inner error: {innerEx.Message}");
                    }
                    return;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ZoomToStation error: {ex.Message}");
            }
        }


        private Task AddLayerShpZRD()
        {
            string shapefilePathZRD = "Z:\\10 DEV\\hydroExplorer\\SHP\\MAPOVERVIEW\\PROJ_TX_ZRD.shp";
            var shapeFileSource = new ShapeFile(shapefilePathZRD, true);

            var shapefileLayer = new Layer("PROJ_TX_ZRD")
            {
                Name = "PROJ_TX_ZRD",
                DataSource = shapeFileSource,
                Tag = new OverViewLayerData { IsMapInfoLayer = true },
                Style = new StyleCollection
                {
                    Styles =
                        {
                            new SymbolStyle
                            {
                                Fill = new Brush(Color.Red),
                                Outline = new Pen(Color.Black, 1),
                                SymbolScale = 0.2
                            },
                            new LabelStyle
                            {
                                LabelColumn = "projname",
                                ForeColor = Color.Black,
                                BackColor = new Brush(_colorLblBackGround),
                                CornerRounding = 3,
                                Font = new Font { FontFamily = "Eras", Size = 10 , Bold = true},
                                HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Center,
                                VerticalAlignment = LabelStyle.VerticalAlignmentEnum.Bottom,
                                MaxVisible = 100,
                                Offset = new Offset { Y = -5 }
                            }
                        }
                }
            };
            _map.Layers.Add(new RasterizingTileLayer(shapefileLayer));
            return Task.CompletedTask;
        }
        
        

        private static List<string> GetAllLayerNames(Client.WmsServerLayer layer)
        {
            var layerNames = new List<string>();
            if (!string.IsNullOrEmpty(layer.Name))
                layerNames.Add(layer.Name);

            foreach (var childLayer in layer.ChildLayers)
                layerNames.AddRange(GetAllLayerNames(childLayer));

            return layerNames;
        }

        private string? FindShapefileByName(string searchText)
        {
            string? dir = Path.GetDirectoryName(_pathHMS);
            if (string.IsNullOrEmpty(dir)) return null;

            string mapsPath = Path.GetFullPath(Path.Combine(dir, "maps"));
            if (!Directory.Exists(mapsPath))
            {
                System.Diagnostics.Debug.WriteLine($"MapView Maps folder not found: {mapsPath}");
                return null;
            }

            string? match = Directory.GetFiles(mapsPath, "*.shp", SearchOption.AllDirectories)
                .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f)
                    .Contains(searchText, StringComparison.OrdinalIgnoreCase));

            if (!Path.Exists(match)) return null;
            return Path.GetFullPath(match);
        }

        private void UtilInfo()
        {
            var wList = _map.Widgets.ToList();
            var widG = wList.ElementAt(2);
            var infoText = ((Mapsui.Widgets.BoxWidgets.TextBoxWidget)widG).Text;

            if (!string.IsNullOrEmpty(infoText))
            {
                char sep = '|';
                List<string> valueList = [.. infoText.Split(sep)];

                string rawRiver = valueList.Where(x => x.Contains("River")).ElementAt(0);
                string valRiver = rawRiver.Split(':').ElementAt(1);
                System.Diagnostics.Debug.WriteLine($"RIVER: {valRiver}");

                string rawReach = valueList.Where(x => x.Contains("Reach")).ElementAt(0);
                string valReach = rawReach.Split(':').ElementAt(1);
                System.Diagnostics.Debug.WriteLine($"REACH: {valReach}");

                string rawRiverStati = valueList.Where(x => x.Contains("RiverStati")).ElementAt(0);
                string valRiverStati = rawRiverStati.Split(':').ElementAt(1);
                System.Diagnostics.Debug.WriteLine($"STA: {valRiverStati} \n");

                UtilInfoTextBox(valRiverStati);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("END UtilInfo!!");
            }
        }

        private static void UtilInfoTextBox(string infoText) =>
            System.Diagnostics.Debug.WriteLine($"infoText: {infoText} \n");

        private static void InitInfoWidgets(Map map)
        {
            map.Widgets.Add(new MapInfoWidget(map, map.Layers.FindLayer("XS")));
            map.Widgets.Add(new MouseCoordinatesWidget());
        }

        


        private static MemoryLayer CreateDarkBackdropLayer()
        {
            var box = new NetTopologySuite.Geometries.Envelope(
                -20037508.34, 20037508.34,
                -20037508.34, 20037508.34);
            var poly = new NetTopologySuite.Geometries.GeometryFactory().ToGeometry(box);

            return new MemoryLayer("Backdrop")
            {
                Features = [new GeometryFeature { Geometry = poly }],
                Style = new VectorStyle
                {
                    Fill = new Brush(new Mapsui.Styles.Color(30, 30, 30, 255)),
                    Outline = null
                }
            };
        }

        private async Task WaitForTilesAsync(CancellationToken token, int timeoutMs = 5000)
        {
            var tcs = new TaskCompletionSource<bool>();

            void OnMapDataChanged(object? sender, EventArgs e)
            {
                var tileLayer = _map.Layers.OfType<TileLayer>().FirstOrDefault();
                if (tileLayer?.Extent != null)
                    tcs.TrySetResult(true);
            }

            _map.DataChanged += OnMapDataChanged;

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));
                await tcs.Task.WaitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) { }
            finally
            {
                _map.DataChanged -= OnMapDataChanged;
            }
        }

        private async Task FadeInTileLayerAsync(CancellationToken token)
        {
            var tileLayer = _map.Layers.OfType<TileLayer>().FirstOrDefault();
            if (tileLayer == null) return;

            tileLayer.Opacity = 0;
            await Dispatcher.InvokeAsync(() => _mapControl.Refresh());
            await WaitForTilesAsync(token, timeoutMs: 800);

            //const int steps = 20;
            //const int intervalMs = 32;

            //for (int i = 1; i <= steps; i++)
            //{
            //    if (token.IsCancellationRequested) break;
            //    double opacity = (double)i / steps;
            //    await Dispatcher.InvokeAsync(() =>
            //    {
            //        tileLayer.Opacity = opacity * 0.55;
            //        _mapControl.Refresh();
            //    });
            //    await Task.Delay(intervalMs, token);
            //}

            await Dispatcher.InvokeAsync(() =>
            {
                tileLayer.Opacity = 0.55;
                _mapControl.Refresh();
            });
        }

    }
}