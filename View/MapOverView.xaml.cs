//using BruTile.Wms;
using BruTile.Predefined;
using BruTile.Web;
using HydroExplorer.Helpers;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Nts.Providers.Shapefile;
using Mapsui.Projections;
using Mapsui.Providers;
using Mapsui.Providers.Wms;
using Mapsui.Styles;
using Mapsui.Tiling.Layers;
using Mapsui.UI.Wpf;
using Mapsui.Widgets.InfoWidgets;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;



namespace HydroExplorer.View
{
    public class OverViewLayerData
    {
        public bool IsMapInfoLayer { get; set; }
    }
    public partial class MapOverView : UserControl
    {
        private readonly MapStateService _mapState;
        private readonly MapControl _mapControl;
        private readonly Map _map;

        private string? _pathXS = string.Empty;
        private string? _pathBNDY = string.Empty;
        private string? _pathHdfA = string.Empty;
        private string? _pathHdfB = string.Empty;
        private string? _pathHMS = string.Empty;
        private string? _pathSubBasins = string.Empty;

        private static readonly Color _colorLblBackGround = new(236, 210, 1, 150);
        private static readonly Color _colorBNDY = new(70, 138, 138, 155);
        private static readonly Color _colorBNDYFill = new(153, 157, 23, 100);
        private static readonly Color _colorTxtLBL = new(0, 0, 0, 155);

        private static readonly Color _colorSPZLine = new(255, 255, 255, 155);
        private static readonly Color _colorSPZFill = new(125, 125, 125, 25);

        private CancellationTokenSource? _resetMapCts;



        public MapOverView()
        {
            InitializeComponent();

            _mapState = App.ServiceProvider.GetRequiredService<MapStateService>();

            _mapControl = new Mapsui.UI.Wpf.MapControl
            {
                Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(71, 71, 73))
            };




            _map = new Map { CRS = "EPSG:3857" };

            // Texas center, reasonable zoom
            var (cx, cy) = SphericalMercator.FromLonLat(-99.0, 31.0);
            _map.Navigator.CenterOnAndZoomTo(new MPoint(cx, cy), 500_000);



            LoggingWidget.ShowLoggingInMap = Mapsui.Widgets.ActiveMode.No;

            AppDomain.CurrentDomain.FirstChanceException += (s, e) =>
            {
                if (e.Exception is IOException && e.Exception.Source is "System.Net.Sockets" or "System.Net.Security")
                    return;
            };

            _mapControl.Map = _map;
            _map.Widgets.Clear();

            _mapControl.Opacity = 0;
            Content = _mapControl;

            Loaded += async (s, e) => { await ResetMap(); };

            EventBus.ProjPathChanged += async path =>
            {
                await Dispatcher.InvokeAsync(async () =>
                {
                    try { await ResetMap(path); }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"ResetMap error: {ex.Message}"); }
                });
            };

        }



        public async Task ResetMap(string projPath = "")
        {
            _resetMapCts?.Cancel();
            _resetMapCts?.Dispose();
            _resetMapCts = new CancellationTokenSource();
            var token = _resetMapCts.Token;

            await Dispatcher.InvokeAsync(() => _mapControl.Opacity = 0);
            await Task.Delay(100, token);

            _map.Layers.Clear();


            var tileSource = KnownTileSources.Create(
                KnownTileSource.EsriWorldDarkGrayBase,
                apiKey: null,
                persistentCache: new BruTile.Cache.FileCache(
                    Path.Combine(Path.GetTempPath(), "HydroExplorer", "TileCache"),
            "png"));

            _map.Layers.Add(new TileLayer(tileSource));

            //_map.Layers.Add(new TileLayer(KnownTileSources.Create(KnownTileSource.EsriWorldDarkGrayBase)));

            try
            {
                await BuildPaths(projPath);
                await AddLayerShpTXCnty();
                await AddLayerShpZRD();
                await AddLayerShpTXZRD();
                await ExportShpBNDY();
                await AddLayerShpBndy();
                await InitView();
                await Dispatcher.InvokeAsync(() => _mapControl.Refresh());
                await WaitForTilesAsync(token);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"ResetMap error: {ex.Message}"); }

            // Wait for first real tile render before fading in
            await WaitForTilesAsync(token);

            await Dispatcher.InvokeAsync(() =>
            {
                _mapControl.BeginAnimation(OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(0, 1,
                        TimeSpan.FromMilliseconds(300)));
            });
        }


        private async Task WaitForTilesAsync(CancellationToken token)
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
                // Timeout fallback — don't hang forever if offline
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

                await tcs.Task.WaitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) { /* timeout or reset — fade in anyway */ }
            finally
            {
                _map.DataChanged -= OnMapDataChanged;
            }
        }

        private async Task InitView()
        {
            if (!Path.Exists(_pathBNDY)) return;

            var shapeFileProvider = new ShapeFile(_pathBNDY, true);
            if (shapeFileProvider.GetExtent() is MRect extent)
            {
                double xx = (extent.MaxX + extent.MinX) / 2;
                double yy = (extent.MaxY + extent.MinY) / 2;

                await Application.Current.Dispatcher.InvokeAsync(() => { },
                    System.Windows.Threading.DispatcherPriority.Loaded);

                var (x, y) = SphericalMercator.FromLonLat(xx, yy);
                _map.Navigator.CenterOn(x, y);
                _map.Navigator.ZoomTo(450);
            }
        }



        private static ILayer CreateDimOverlayLayer(byte opacity = 180)
        {
            var box = new NetTopologySuite.Geometries.Envelope(
                -20037508.34, 20037508.34,
                -20037508.34, 20037508.34);

            var poly = new NetTopologySuite.Geometries.GeometryFactory()
                .ToGeometry(box);

            var layer = new MemoryLayer("DimOverlay")
            {
                Features = new[] { new GeometryFeature { Geometry = poly } },
                Style = new VectorStyle
                {
                    Fill = new Brush(new Color(30, 30, 30, opacity)), // dark gray, semi-transparent
                    Outline = null
                }
            };
            return layer;
        }


        private static void UtilInfo(Map map)
        {
            var wList = map.Widgets.ToList();
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
                System.Diagnostics.Debug.WriteLine("END UtilInfo!! \n");
            }
        }


        private static void UtilInfoTextBox(string infoText)
        {
            System.Diagnostics.Debug.WriteLine($"infoText: {infoText} \n");
        }


        private void InitInfoWidgets()
        {
            //var infoLayer = map.Layers.OfType<MemoryLayer>().FirstOrDefault(l => l.infoLayer);
            //_targetLayer = map.Layers.FirstOrDefault(f => f.Name == "Layer 3") as WritableLayer;
            //_editingWidget = map.Widgets.OfType<EditingWidget>().Single();
            //map.Widgets.Add(new MapInfoWidget(map, [map.Layers.Last()]));

            _map.Widgets.Add(new MapInfoWidget(_map, _map.Layers.FindLayer("XS")));
            _map.Widgets.Add(new MouseCoordinatesWidget());
            //map.Widgets.Add(CreateSelectButton());
        }


        private Task AddLayerShpTXSpz()
        {
            string shapefilePath = "Z:\\10 DEV\\hydroExplorer\\SHP\\MAPOVERVIEW\\TX_SPZ.shp";
            if (!Path.Exists(shapefilePath)) return Task.CompletedTask;

            var shapefileSource = new ShapeFile(shapefilePath, true);
            //var dataSource = new ProjectingProvider(shapeFileProvider) { CRS = "EPSG:3857" };

            var shapefileLayer = new Layer("TX_SPZ")
            {
                Name = "TX_SPZ",
                DataSource = shapefileSource,
                Tag = new OverViewLayerData { IsMapInfoLayer = true },
                Style = new StyleCollection
                {
                    Styles =
                    {
                        new VectorStyle
                        {
                            Outline = new Pen(_colorSPZLine, 20),
                            Fill = new Brush(_colorSPZFill)
                        },
                        new LabelStyle
                        {
                            Enabled = true,
                            LabelColumn = "ZONE",
                            BackColor = new Brush(Color.Transparent),
                            ForeColor = new Color(_colorTxtLBL),
                            Font = new Font { FontFamily = "Eras", Size = 18, Bold = false },
                            HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Center,
                            VerticalAlignment = LabelStyle.VerticalAlignmentEnum.Center,
                            MaxVisible = 1000,
                        }
                }
                }
            };
            _map.Layers.Add(new RasterizingTileLayer(shapefileLayer));

            return Task.CompletedTask;
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



        private Task AddLayerShpTXZRD()
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


        public static ILayer CreateProjectedShapefileLayer(string shapefilePath)
        {
            var provider = new ShapeFile(shapefilePath, true) { CRS = "EPSG:4326" };
            // Wrap in a ProjectingProvider to reproject to Web Mercator (3857)
            var projectingProvider = new ProjectingProvider(provider) { CRS = "EPSG:3857" };

            var vectorLayer = new Layer("ShapefileLayer")
            {
                DataSource = projectingProvider,
                Style = new VectorStyle
                {
                    Fill = new Brush(Color.FromArgb(100, 255, 80, 0)),
                    Outline = new Pen(Color.Red, 1f)
                }
            };
            return new RasterizingTileLayer(vectorLayer);
        }


        private Task AddLayerShpTXCnty()
        {
            string shapefilePathCnty = "Z:\\10 DEV\\hydroExplorer\\SHP\\MAPOVERVIEW\\TX_CNTY.shp";
            if (!Path.Exists(shapefilePathCnty)) return Task.CompletedTask;

            var shapeFileSource = new ShapeFile(shapefilePathCnty);

            var shapefileLayer = new Layer("TX_CNTY")
            {
                Name = "TX_CNTY",
                DataSource = shapeFileSource,
                Tag = new OverViewLayerData { IsMapInfoLayer = true },
                Style = new StyleCollection
                {
                    Styles =
                        {
                            new VectorStyle
                            {
                                Fill = new Brush(Color.Transparent),
                                Outline = new Pen(Color.Black, 2),
                                Opacity = 0.3f
                            },
                            new LabelStyle
                            {
                                Enabled = true,
                                LabelColumn = "NAME",
                                BackColor = new Brush(Color.Transparent),
                                ForeColor = new Color(_colorTxtLBL),
                                Font = new Font { FontFamily = "Eras", Size = 14 , Bold = true},
                                HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Center,
                                VerticalAlignment = LabelStyle.VerticalAlignmentEnum.Center,
                                MaxVisible = 1000,
                            }
                        }
                }
            };
            _map.Layers.Add(new RasterizingTileLayer(shapefileLayer));
            return Task.CompletedTask;
        }


        private Task AddLayerShpBndy()
        {
            if (!Path.Exists(_pathBNDY)) return Task.CompletedTask;

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
                                Fill = new Brush(_colorBNDYFill),
                                Outline = new Pen(Color.Black, 2),
                            },
                            new VectorStyle
                            {
                                Fill = new Brush(_colorBNDYFill),
                                Outline = new Pen(_colorBNDY, 7),
                            },
                        }
                }
            };
            _map.Layers.Add(new RasterizingTileLayer(shapefileLayer));
            return Task.CompletedTask;
        }


        private Task AddLayerShpXS()
        {
            if (!Path.Exists(_pathXS)) return Task.CompletedTask;

            var shapeFileProvider = new ShapeFile(_pathXS) { CRS = "EPSG:4326" };
            var dataSource = new ProjectingProvider(shapeFileProvider) { CRS = "EPSG:3857" };
            var shapefileLayer = new Layer("XS")
            {
                Name = "XS",
                DataSource = dataSource,
                Tag = new OverViewLayerData { IsMapInfoLayer = true },
                Style = new StyleCollection
                {
                    Styles =
                        {
                            new VectorStyle
                            {
                                Outline = new Pen(Color.Red, 2)
                            },
                        }
                }
            };
            _map.Layers.Add(new RasterizingTileLayer(shapefileLayer));
            return Task.CompletedTask;
        }

        private static VectorStyle CreateThemeZones()
        {
            return new VectorStyle
            {
                Outline = new Pen(Color.Gray),
                Fill = new Brush(Color.Gray),
                Opacity = 0.15f
            };
        }


        private static List<string> GetAllLayerNames(Client.WmsServerLayer layer)
        {
            var layerNames = new List<string>();
            if (!string.IsNullOrEmpty(layer.Name))
            {
                layerNames.Add(layer.Name);
            }

            foreach (var childLayer in layer.ChildLayers)
            {
                layerNames.AddRange(GetAllLayerNames(childLayer));
            }
            return layerNames;
        }





        public async Task ExportShpBNDY()
        {
            if (string.IsNullOrEmpty(_pathSubBasins) || !File.Exists(_pathSubBasins)) return;
            if (string.IsNullOrEmpty(_pathBNDY)) return;

            if (File.Exists(_pathBNDY) && File.Exists(_pathSubBasins))
            {
                var newer = GetNewerFile(_pathBNDY, _pathSubBasins);
                if (newer?.FullName == _pathBNDY) return;
            }

            string pathTMP = GetTempShpPath();

            try
            {
                await ExporterBndy.ExportBNDY(
                    pathSubBasins: _pathSubBasins,
                    pathTMP: pathTMP,           // ← pass it in
                    pathBNDY: _pathBNDY
                );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ExportBNDY error: {ex.Message}");
            }
            finally
            {
                GISUtil.DeleteShapefileIfExists(pathTMP);
            }
        }


        public static FileInfo? GetNewerFile(string pathA, string pathB)
        {
            if (pathA == "" && pathB == "") return null;

            var fileA = new FileInfo(pathA);
            var fileB = new FileInfo(pathB);

            if (!fileA.Exists) throw new FileNotFoundException($"File not found: {pathA}");
            if (!fileB.Exists) throw new FileNotFoundException($"File not found: {pathB}");

            return fileA.LastWriteTime > fileB.LastWriteTime ? fileA : fileB;
        }











        private async Task BuildPaths(string projPathOverride = "")
        {
            string? projPath = string.Empty;

            try
            {
                var settingsRepo = App.ServiceProvider.GetRequiredService<IUserSettingsRepo>();

                var settings = !string.IsNullOrEmpty(projPathOverride)
                    ? await settingsRepo.GetSettingsFresh()
                    : await settingsRepo.GetSettings();

                projPath = !string.IsNullOrEmpty(projPathOverride)
                    ? projPathOverride
                    : settings.ProjPath;

                if (string.IsNullOrEmpty(projPath)) return;

                if (!settings.Projects.TryGetValue(projPath, out var projSettings))
                {
                    System.Diagnostics.Debug.WriteLine($"BuildPaths: no settings found for '{projPath}'.");
                    return;
                }

                string tmpPath = Path.GetFullPath(Path.Combine(projPath, ".."));
                string spatialPath = Path.Combine(tmpPath, "Spatial");

                if (!Directory.Exists(spatialPath))
                    Directory.CreateDirectory(spatialPath);

                _pathXS = Path.Combine(spatialPath, "XS.shp");
                _pathBNDY = Path.Combine(spatialPath, "BNDY.shp");

                _pathHdfA = projSettings.HdfPathA;
                _pathHdfB = projSettings.HdfPathB;
                _pathHMS = projSettings.HmsPath;



                // ── Auto-discover HdfPathB if missing ────────────────────────
                if (string.IsNullOrEmpty(_pathHdfB) || !File.Exists(_pathHdfB))
                {
                    string projDir = Directory.Exists(settings.ProjPath)
                        ? settings.ProjPath
                        : Path.GetDirectoryName(settings.ProjPath) ?? string.Empty;

                    _pathHdfB = Directory.GetFiles(projDir, "*.hdf")
                        .FirstOrDefault(f => HdfFileRegex().IsMatch(Path.GetFileName(f)))
                        ?? string.Empty;

                    if (!string.IsNullOrEmpty(_pathHdfB))
                    {
                        settings.Projects[settings.ProjPath].HdfPathB = _pathHdfB;
                        await settingsRepo.SaveSettings(settings);
                        System.Diagnostics.Debug.WriteLine($"BuildPaths: auto-set HdfPathB={_pathHdfB}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("BuildPaths: no .pxx.hdf file found.");
                    }
                }

                if (string.IsNullOrEmpty(_pathHdfA))
                {
                    System.Diagnostics.Debug.WriteLine("BuildPaths: no .pxx.hdf file found.");
                }

                // ── Warn if no HDF found at all ──────────────────────────────────────
                if ((string.IsNullOrEmpty(_pathHdfA) || !File.Exists(_pathHdfA)) &&
                    (string.IsNullOrEmpty(_pathHdfB) || !File.Exists(_pathHdfB)))
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show(
                            "No HDF files were found for this project.\n\nVerify that HEC-RAS has been run and output files exist in the project directory.",
                            "No HDF Files Found",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    });
                }


                //if (!await EnsureHmsPath(projPath, settingsRepo, settings)) return;

                if (!string.IsNullOrEmpty(_pathHMS))
                    EventBus.PublishHmsPathChanged(_pathHMS);

                _pathSubBasins = FindShapefileByName("basin") ?? string.Empty;

                EventBus.PublishGeometryPathsResolved(
                    pathSubBasins: _pathSubBasins,
                    pathXS: _pathXS ?? string.Empty,
                    pathBNDY: _pathBNDY ?? string.Empty
                );




                bool canPublish = !string.IsNullOrEmpty(projPath)
                    && ((!string.IsNullOrEmpty(_pathHdfA) && File.Exists(_pathHdfA))
                     || (!string.IsNullOrEmpty(_pathHdfB) && File.Exists(_pathHdfB)));

                if (!canPublish)
                {
                    System.Diagnostics.Debug.WriteLine("BuildPaths: skipping publish, no valid HDF found.");
                    return;
                }

                _mapState.Publish(new ProjectPaths(
                    ProjPath: projPath,
                    PathXS: _pathXS ?? "",
                    PathCL: Path.Combine(Path.GetDirectoryName(_pathXS ?? "") ?? "", "CL.shp"),
                    PathBNDY: _pathBNDY ?? "",
                    PathHdfA: _pathHdfA ?? "",
                    PathHdfB: _pathHdfB ?? "",
                    PathHMS: _pathHMS ?? "",
                    PathSubBasins: _pathSubBasins ?? ""
                ));
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("BuildPaths cancelled.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BuildPaths error: {ex.Message}");
            }
        }












        private async Task<bool> EnsureHmsPath(string projPath, IUserSettingsRepo settingsRepo, UserSettings settings)
        {
            if (!string.IsNullOrEmpty(_pathHMS)) return true;

            bool hasAnyHdf = (!string.IsNullOrEmpty(_pathHdfA) && File.Exists(_pathHdfA))
                          || (!string.IsNullOrEmpty(_pathHdfB) && File.Exists(_pathHdfB));
            if (!hasAnyHdf)
            {
                System.Diagnostics.Debug.WriteLine("EnsureHmsPath: skipping, no valid HDF found.");
                return false;
            }

            var result = MessageBox.Show(
                "HMS path is not set. Would you like to select it now?",
                "HMS Path Missing",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return false;

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select HMS Run File",
                Filter = "HMS Run Files (*.run)|*.run|All Files (*.*)|*.*",
                InitialDirectory = Path.GetFullPath(
                    Path.Combine(Path.GetDirectoryName(projPath) ?? string.Empty, ".."))
            };

            if (dialog.ShowDialog() != true) return false;

            string selectedPath = dialog.FileName;
            string projRoot = Path.GetFullPath(
                Path.Combine(Path.GetDirectoryName(projPath) ?? string.Empty, ".."));

            //if (!selectedPath.StartsWith(projRoot, StringComparison.OrdinalIgnoreCase))
            //{
            //    MessageBox.Show(
            //        $"Selected file should be within the project root folder:\n{projRoot}",
            //        "Verify HMS Path",
            //        MessageBoxButton.OK,
            //        MessageBoxImage.Warning);
            //    //return false;
            //}

            _pathHMS = selectedPath;

            settings.Projects[settings.ProjPath].HmsPath = _pathHMS;
            await settingsRepo.SaveSettings(settings);

            EventBus.PublishHmsPathChanged(_pathHMS);
            EventBus.PublishRunPath(_pathHMS);

            return true;
        }








        private string? FindShapefileByName(string searchText)
        {
            if (string.IsNullOrEmpty(_pathHMS))
            {
                System.Diagnostics.Debug.WriteLine("FindShapefileByName: _pathHMS is empty.");
                return null;
            }

            string? dir = Path.GetDirectoryName(_pathHMS);
            if (string.IsNullOrEmpty(dir))
            {
                System.Diagnostics.Debug.WriteLine($"FindShapefileByName: Could not get directory from '{_pathHMS}'.");
                return null;
            }

            string? mapsPath = Directory.EnumerateDirectories(dir, "maps", SearchOption.AllDirectories)
                .FirstOrDefault();

            if (mapsPath == null)
            {
                System.Diagnostics.Debug.WriteLine($"FindShapefileByName: 'maps' folder not found under '{dir}'.");
                return null;
            }

            var match = Directory.GetFiles(mapsPath, "*.shp", SearchOption.AllDirectories)
                .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f)
                    .Contains(searchText, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                System.Diagnostics.Debug.WriteLine($"FindShapefileByName: No shapefile containing '{searchText}' found in '{mapsPath}'.");
                return null;
            }

            return Path.GetFullPath(match);
        }


        private static string GetTempShpPath()
        {
            string tmpDir = @"C:\Temp";
            string uniqueName = $"tmp_{Guid.NewGuid():N}.shp";
            return Path.Combine(tmpDir, uniqueName);
        }


        [GeneratedRegex(@"\.p\d+\.hdf$", RegexOptions.IgnoreCase, "en-US")]
        private static partial Regex HdfFileRegex();

    }


}
