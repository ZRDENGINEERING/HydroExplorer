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
using ProjNet.CoordinateSystems.Transformations;
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
                Style = new VectorStyle
                {
                    Outline = new Pen(Color.LightYellow, 2),
                    Opacity = 0.1f,
                    Fill = null
                }
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

            // Plan A drives which plan's 2D results (if any) are shown — refresh
            // independently of a full ResetMapView so switching plans updates it.
            EventBus.HdfPathChanged += (hdfPathA, hdfPathB) =>
            {
                Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        _pathHdfA = hdfPathA;
                        await UpdateLayer2DResults(hdfPathA);
                        _mapControl.Refresh();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"MapView 2D layer refresh error:\n{ex}");
                    }
                });
            };

            Unloaded += (s, e) =>
            {
                _mapState.PathsReady -= OnPathsReady;
                EventBus.MapOverViewReady -= OnMapOverViewReady;
            };

            _mapState.PathsReady += OnPathsReady;
            if (_mapState.CurrentPaths != null)
            {
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

                    _mapState.CurrentXsPath = _pathXS;


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

            var lyr_bing = new TileLayer(tileSource) { Opacity = 0 };
            _map.Layers.Add(lyr_bing);

            try
            {
                await AddLayerShpTXCnty();
                await AddLayerShpBndy();
                await AddLayerShpProjects();

                var plotVm = App.ServiceProvider.GetRequiredService<PlotViewModel>();
                if (plotVm.WselData != null)
                    await AddLayerShpXS(plotVm.WselData);
                else
                    System.Diagnostics.Debug.WriteLine("ResetMapView: WselData null — skipping XS layer.");

                await AddLayerShpRiver();

                // Isolated from the rest of map setup: a failure reading 2D
                // results (untested against real files as of this patch) must
                // never take down tiles/XS/river/gage rendering with it.
                try
                {
                    await UpdateLayer2DResults(_pathHdfA);
                }
                catch (Exception ex2d)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"UpdateLayer2DResults (ResetMapView) failed — leaving map otherwise intact:\n{ex2d}");
                }

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
                    var (cx, cy) = SphericalMercator.FromLonLat(-99.0, 31.0);
                    _map.Navigator.CenterOnAndZoomTo(new MPoint(cx, cy), 3000);
                    _mapControl.Refresh();
                    return;
                }

                var shapeFileProvider = new ShapeFile(centerPath, true);

                double[] zoomToCoords = MapOverView.ReprojectHelper(centerPath);

                var (x, y) = SphericalMercator.FromLonLat(zoomToCoords[0], zoomToCoords[1]);

                _map.Navigator.CenterOn(x, y);
                _map.Navigator.ZoomTo(350);

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

            var wselIndex = new Dictionary<string, WSELTableOxy>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in wselData)
            {
                string key = w.RiverSta?.Trim() ?? string.Empty;
                wselIndex.TryAdd(key, w);
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
                    Line = new Pen(lineColor, 0),
                    Outline = new Pen(lineColor, 0.5),
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
            string shapefilePathTXCnty = Path.Combine(AppContext.BaseDirectory, "SHP", "TX_CNTY.shp");

            if (!Path.Exists(shapefilePathTXCnty)) return Task.CompletedTask;

            var shapeFileProvider = new ShapeFile(shapefilePathTXCnty) { CRS = "EPSG:4326" };
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

        /// <summary>
        /// Renders 2D cell results (max WSE where available, else min terrain) as a
        /// point layer, colored by value. Replaces any existing "2D_RESULTS" layer.
        /// No-ops (removing any stale layer) when the plan has no 2D flow areas,
        /// isn't set, or its coordinate system can't be resolved.
        /// </summary>
        private async Task UpdateLayer2DResults(string? planHdfPath)
        {
            var existing = _map.Layers.FirstOrDefault(l => l.Name == "2D_RESULTS");
            if (existing != null)
            {
                _map.Layers.Remove(existing);
                (existing as IDisposable)?.Dispose();
            }

            if (string.IsNullOrEmpty(planHdfPath) || !File.Exists(planHdfPath))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"UpdateLayer2DResults: no plan HDF path / file missing ('{planHdfPath}') — skipping.");
                return;
            }

            string? geomHdf = HecRasPrjReader.ResolveGeomHdf(planHdfPath);
            if (geomHdf == null)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"UpdateLayer2DResults: could not resolve geometry HDF for '{planHdfPath}' — skipping.");
                return;
            }

            var (has1D, has2D) = HecRasHdfReader.GetModelDimensions(geomHdf);
            System.Diagnostics.Debug.WriteLine(
                $"UpdateLayer2DResults: geom='{Path.GetFileName(geomHdf)}' has1D={has1D} has2D={has2D}");
            if (!has2D) return;

            // HecRas2DHdfReader.Read() calls into PureHDF, which does not appear
            // to be safe to run concurrently with the rest of the app's own HDF
            // reads (RAS Tables loading, shapefile export, etc. on the UI thread)
            // — running it via Task.Run intermittently returned 0 cells for a
            // file that read fine synchronously. Keep this call on the calling
            // thread; only the pure-math reprojection/styling below (no shared
            // native library state) is safe to offload.
            var results = HecRas2DHdfReader.Read(geomHdf, planHdfPath);
            System.Diagnostics.Debug.WriteLine(
                $"UpdateLayer2DResults: HecRas2DHdfReader.Read returned {results.Cells.Count} cells.");
            if (results.Cells.Count == 0) return;

            // No need to render every cell for a first-pass visual read — sample
            // every Nth one. Cuts CRS resolution, reprojection, and rendering
            // cost by the same factor.
            const int cellDisplayStride = 10;
            var displayCells = cellDisplayStride > 1
                ? results.Cells.Where((c, i) => i % cellDisplayStride == 0).ToList()
                : results.Cells;
            System.Diagnostics.Debug.WriteLine(
                $"UpdateLayer2DResults: displaying {displayCells.Count} of {results.Cells.Count} cells " +
                $"(every {cellDisplayStride}).");

            string? srcWkt = await ResolveCellsSourceWktAsync(displayCells, geomHdf);
            System.Diagnostics.Debug.WriteLine(
                $"UpdateLayer2DResults: resolved source WKT: {(srcWkt == null ? "(none)" : "found")}");
            if (string.IsNullOrEmpty(srcWkt))
            {
                System.Diagnostics.Debug.WriteLine(
                    "UpdateLayer2DResults: could not resolve a coordinate system for the 2D cells — skipping layer.");
                return;
            }

            string tgtWkt = GISUtil.FetchWkt(4326);
            if (string.IsNullOrEmpty(tgtWkt))
            {
                System.Diagnostics.Debug.WriteLine("UpdateLayer2DResults: FetchWkt(4326) failed — skipping.");
                return;
            }

            var transform = GISUtil.CreateTransformation(srcWkt, tgtWkt);

            // Reprojecting and styling up to 100k+ points is CPU-bound — do it
            // off the UI thread. Only _map.Layers.Add below touches UI state.
            var (features, hasResults) = await Task.Run(
                () => BuildCellFeatures(displayCells, transform));

            var layer2D = new MemoryLayer
            {
                Name = "2D_RESULTS",
                Features = features
            };

            // 100k+ individually-styled points redrawn every frame is what was
            // making pan/zoom slow — wrap in a RasterizingTileLayer so Mapsui
            // renders once per tile and caches it, instead of re-drawing every
            // point on every pan/zoom.
            _map.Layers.Add(new RasterizingTileLayer(layer2D));
            System.Diagnostics.Debug.WriteLine(
                $"UpdateLayer2DResults: added 2D_RESULTS layer with {features.Count} points (colored by {(hasResults ? "Max WSE" : "Min Terrain")}).");
        }

        /// <summary>
        /// Resolves the source coordinate system for raw 2D cell coordinates as a
        /// WKT string: cached ProjectSettings.SourceEpsg first (shared with the
        /// shapefile exporters), else the geometry HDF's own stored projection
        /// used VERBATIM (RAS 2D geometry is commonly set up in a custom statewide
        /// projection like "NAD83 / Texas Centric Albers Equal Area" that has no
        /// EPSG code at all — forcing it through TryGetEpsgFromWkt just to look the
        /// WKT back up again loses that projection entirely), else a Texas State
        /// Plane guess from the cells' extent as a last resort.
        /// </summary>
        private async Task<string?> ResolveCellsSourceWktAsync(List<HecRas2DCell> cells, string geomHdfPath)
        {
            string projKey = PathHelpers.NormalizeProjKey(_currentProjPath ?? string.Empty);
            var settings = await _settingsRepo.GetSettings();

            if (!string.IsNullOrEmpty(projKey) &&
                settings.Projects.TryGetValue(projKey, out var existing) &&
                existing.SourceEpsg is > 0)
            {
                return GISUtil.FetchWkt(existing.SourceEpsg.Value);
            }

            string? wkt = HecRas2DHdfReader.TryReadProjectionWkt(geomHdfPath);
            System.Diagnostics.Debug.WriteLine(
                wkt == null
                    ? "ResolveCellsSourceWktAsync: no projection attribute found on geometry HDF."
                    : $"ResolveCellsSourceWktAsync: found projection attribute: {wkt}");

            if (!string.IsNullOrEmpty(wkt))
            {
                // Opportunistically cache an EPSG code too, purely so the shapefile
                // exporters can reuse it — but this WKT is authoritative regardless
                // of whether it happens to map to one.
                int maybeEpsg = GISUtil.TryGetEpsgFromWkt(wkt);
                if (maybeEpsg > 0 && !string.IsNullOrEmpty(projKey))
                {
                    if (!settings.Projects.TryGetValue(projKey, out var proj))
                        proj = settings.Projects[projKey] = new ProjectSettings { ProjPath = projKey };
                    proj.SourceEpsg = maybeEpsg;
                    await _settingsRepo.SaveSettings(settings);
                }
                return wkt;
            }

            var extent = new NetTopologySuite.Geometries.Envelope();
            foreach (var c in cells)
                extent.ExpandToInclude(c.CenterX, c.CenterY);
            System.Diagnostics.Debug.WriteLine(
                $"ResolveCellsSourceWktAsync: raw cell extent X[{extent.MinX:0.##}..{extent.MaxX:0.##}] " +
                $"Y[{extent.MinY:0.##}..{extent.MaxY:0.##}], centroid=({(extent.MinX + extent.MaxX) / 2:0.##}, {(extent.MinY + extent.MaxY) / 2:0.##})");

            int guessedEpsg = GISUtil.GuessTexasStatePlaneZone(extent);
            if (guessedEpsg <= 0) return null;

            if (!string.IsNullOrEmpty(projKey))
            {
                if (!settings.Projects.TryGetValue(projKey, out var proj2))
                    proj2 = settings.Projects[projKey] = new ProjectSettings { ProjPath = projKey };
                proj2.SourceEpsg = guessedEpsg;
                await _settingsRepo.SaveSettings(settings);
            }

            return GISUtil.FetchWkt(guessedEpsg);
        }

        /// <summary>
        /// Reprojects cells to map coordinates and builds styled point features.
        /// Runs off the UI thread (see caller) — must not touch _map or any WPF
        /// state. Uses a small cached palette of styles instead of allocating a
        /// new Color/Brush/SymbolStyle per cell; with 100k+ cells that was a
        /// meaningful chunk of both the CPU time and GC pressure.
        /// </summary>
        private static (List<GeometryFeature> features, bool hasResults) BuildCellFeatures(
            List<HecRas2DCell> cells, ICoordinateTransformation transform)
        {
            bool hasResults = cells.Any(c => !double.IsNaN(c.MaxWSE));

            double minVal = double.MaxValue, maxVal = double.MinValue;
            foreach (var c in cells)
            {
                double v = hasResults ? c.MaxWSE : c.MinTerrain;
                if (double.IsNaN(v)) continue;
                if (v < minVal) minVal = v;
                if (v > maxVal) maxVal = v;
            }
            double range = maxVal > minVal ? maxVal - minVal : 1;

            const int paletteSize = 64;
            var palette = new SymbolStyle[paletteSize];
            for (int i = 0; i < paletteSize; i++)
            {
                palette[i] = new SymbolStyle
                {
                    SymbolType = SymbolType.Rectangle,
                    Fill = new Brush(ValueToRampColor(i / (double)(paletteSize - 1))),
                    Outline = null,
                    SymbolScale = 0.04
                };
            }
            var naStyle = new SymbolStyle
            {
                SymbolType = SymbolType.Rectangle,
                Fill = new Brush(new Color(120, 120, 120, 160)),
                Outline = null,
                SymbolScale = 0.04
            };

            var features = new List<GeometryFeature>(cells.Count);
            foreach (var cell in cells)
            {
                var srcCoord = new NetTopologySuite.Geometries.Coordinate(cell.CenterX, cell.CenterY);
                var lonLat = GISUtil.Reproject(srcCoord, transform);
                var (mx, my) = SphericalMercator.FromLonLat(lonLat.X, lonLat.Y);

                double val = hasResults ? cell.MaxWSE : cell.MinTerrain;

                var feature = new GeometryFeature
                {
                    Geometry = new NetTopologySuite.Geometries.Point(mx, my)
                };
                feature["label"] = hasResults
                    ? $"{cell.AreaName} cell {cell.CellIndex}\nMax WSE: {cell.MaxWSE:0.00}\n" +
                      $"Max Depth: {cell.MaxDepth:0.00}\nMax Vel: {cell.MaxVelMag:0.00}"
                    : $"{cell.AreaName} cell {cell.CellIndex}\nMin Terrain: {cell.MinTerrain:0.00}";

                if (double.IsNaN(val))
                {
                    feature.Styles.Add(naStyle);
                }
                else
                {
                    int idx = (int)Math.Round(Math.Clamp((val - minVal) / range, 0, 1) * (paletteSize - 1));
                    feature.Styles.Add(palette[idx]);
                }

                features.Add(feature);
            }

            return (features, hasResults);
        }

        /// <summary>
        /// Simple blue → cyan → yellow → red ramp for a normalized [0,1] value —
        /// low = deep blue, high = red. Good enough for a first-pass 2D overlay;
        /// swap for a proper legend/gradient control later if this needs to be
        /// more than a quick visual read.
        /// </summary>
        private static Color ValueToRampColor(double t)
        {
            t = Math.Clamp(t, 0, 1);

            (byte r, byte g, byte b) low = (0, 0, 180);
            (byte r, byte g, byte b) midLow = (0, 200, 200);
            (byte r, byte g, byte b) midHigh = (255, 255, 0);
            (byte r, byte g, byte b) high = (220, 0, 0);

            static byte Lerp(byte a, byte b, double f) => (byte)(a + (b - a) * f);

            (byte r, byte g, byte b) c;
            if (t < 1.0 / 3)
            {
                double f = t / (1.0 / 3);
                c = (Lerp(low.r, midLow.r, f), Lerp(low.g, midLow.g, f), Lerp(low.b, midLow.b, f));
            }
            else if (t < 2.0 / 3)
            {
                double f = (t - 1.0 / 3) / (1.0 / 3);
                c = (Lerp(midLow.r, midHigh.r, f), Lerp(midLow.g, midHigh.g, f), Lerp(midLow.b, midHigh.b, f));
            }
            else
            {
                double f = (t - 2.0 / 3) / (1.0 / 3);
                c = (Lerp(midHigh.r, high.r, f), Lerp(midHigh.g, high.g, f), Lerp(midHigh.b, high.b, f));
            }

            return new Color(c.r, c.g, c.b, 255);
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


        // ExportShpXS/ExportShpRiver and their supporting CRS-resolution
        // helpers were removed — this duplicated GeometryExportCoordinator
        // almost exactly. Export now happens once, centrally, in
        // MapOverView.BuildPaths before ProjectPaths is ever published, so
        // by the time OnPathsReady fires here, _pathXS/_PathRiver already
        // point at files that exist (or the caller decided not to export).

        private async Task ExportAndReloadCLAsync(CancellationToken token)
        {
            // Export already happened upstream — this just picks up the
            // result if the River layer wasn't loaded yet when OnPathsReady/
            // ResetMapView first ran (timing race between MapOverView and
            // MapView initialization).
            if (File.Exists(_PathRiver))
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
                                Line = new Pen(highlightColor, 2),
                                Outline = new Pen(highlightColor, 2),
                                Fill = null,
                                Opacity = 0.6f
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


        private Task AddLayerShpProjects()
        {
            string shapefilePathTXProjs = Path.Combine(AppContext.BaseDirectory, "SHP", "TX_PROJS.shp");
            var shapeFileSource = new ShapeFile(shapefilePathTXProjs, true);

            var shapefileLayer = new Layer()
            {
                Name = "TX_PROJS",
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