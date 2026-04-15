//using BruTile.Wms;
using HydroExplorer.Helpers;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
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
        public readonly EditingWidget? _editingWidget;
        private readonly MapControl _mapControl;
        private readonly Map _map;

        private string? _pathXS = string.Empty;
        private string? _pathBNDY = string.Empty;
        private string? _pathHdfA = string.Empty;
        private string? _pathHdfB = string.Empty;
        private string? _pathHMS = string.Empty;
        private string? _pathSubBasins = string.Empty;

        private static readonly Color _lblBackGroundColor = new(236, 210, 1, 150);
        private static readonly Color _vecBNDYColor = new(70, 138, 138, 155);
        private static readonly Color _vecBNDYFill = new(128, 128, 128, 50);
        private static readonly Color _txtLBLColor = new(0, 0, 0, 155);

        private CancellationTokenSource? _resetMapCts;


        public MapOverView()
        {
            InitializeComponent();

            _mapControl = new Mapsui.UI.Wpf.MapControl();
            _map = new Map { CRS = "EPSG:3857" };

            LoggingWidget.ShowLoggingInMap = Mapsui.Widgets.ActiveMode.No;

            //var source = KnownTileSources.Create(KnownTileSource.BingAerial);
            //TileLayer bingSat = new TileLayer(source);
            //bingSat.Name = "Bing Aerial";
            //map.Layers.Add(bingSat);

            AppDomain.CurrentDomain.FirstChanceException += (s, e) =>
            {
                if (e.Exception is IOException && e.Exception.Source is "System.Net.Sockets" or "System.Net.Security")
                    return; // suppress tile network noise
            };

            _mapControl.Map = _map;

            //SATX
            //var (x, y) = SphericalMercator.FromLonLat(-98.4936, 29.4241);
            //TX
            //var (x, y) = SphericalMercator.FromLonLat(-99.1440, 31.4928);
            //map.Navigator.CenterOn(x, y);

            //var shpPath = "Z:\\10 DEV\\hydroExplorer\\SHP\\TX_CNTY.shp";
            //_map.Layers.Add(CreateProjectedShapefileLayer(shpPath));


            Content = _mapControl;

            Loaded += async (s, e) =>
            {
                await ResetMap();
            };

            //InitInfoWidgets();
            //AddLayerShpZRD();

            EventBus.ProjPathChanged += async path =>
            {
                await Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        await ResetMap(path);
                    }
                    catch (OperationCanceledException)
                    {
                        System.Diagnostics.Debug.WriteLine("ResetMap cancelled.");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"ResetMap error: {ex.Message}");
                    }
                });
            };
        }



        public async Task ResetMap(string projPath = "")
        {
            _resetMapCts?.Cancel();
            _resetMapCts?.Dispose();
            _resetMapCts = new CancellationTokenSource();
            var token = _resetMapCts.Token;

            await Task.Delay(100, token);
            _map.Layers.Clear();

            var lyr_osm = Mapsui.Tiling.OpenStreetMap.CreateTileLayer();
            lyr_osm.Opacity = 0.6;
            _map.Layers.Add(lyr_osm);

            try
            {
                await BuildPaths(projPath);
                await ExportXStoShp();
                await ExportBNDY();
                await AddLayerShpBndy();
                await AddLayerCnty();
                await InitView();
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("ResetMap cancelled - newer project selected.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ResetMap error: {ex.Message}");
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

                var (x, y) = SphericalMercator.FromLonLat(xx, yy);
                _map.Navigator.CenterOn(x, y);

                _map.Navigator.ZoomTo(450);
            }
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


        private void AddLayerShpTxPgon()
        {
            string shapefilePath = "Z:\\10 DEV\\hydroExplorer\\SHP\\TX_PGON.shp";
            var shapeFileProvider = new ShapeFile(shapefilePath, true) { CRS = "f:4326" };
            var dataSource = new ProjectingProvider(shapeFileProvider) { CRS = "EPSG:3857" };

            var shapefileLayer = new Mapsui.Layers.Layer("Zones")
            {
                DataSource = dataSource,
                Style = CreateThemeZones()
            };
            _map.Layers.Add(new RasterizingTileLayer(shapefileLayer));
        }


        private void AddLayerShpTxCnty()
        {
            string shapefilePath = "Z:\\10 DEV\\hydroExplorer\\SHP\\TX_PGON.shp";
            var shapeFileProvider = new ShapeFile(shapefilePath, true) { CRS = "EPSG:4326" };
            var dataSource = new ProjectingProvider(shapeFileProvider) { CRS = "EPSG:3857" };

            var shapefileLayer = new Mapsui.Layers.Layer("Zones")
            {
                DataSource = dataSource,
                Style = CreateThemeZones()
            };
            _map.Layers.Add(shapefileLayer);
        }


        private void AddLayerShpZRD()
        {
            string shapefilePathZRD = "Z:\\10 DEV\\hydroExplorer\\SHP\\ZRD_TX.shp";
            var shapeFileProvider = new ShapeFile(shapefilePathZRD, true) { CRS = "EPSG:4326" };
            var dataSource = new ProjectingProvider(shapeFileProvider) { CRS = "EPSG:3857" };
            var shapefileLayer = new Mapsui.Layers.Layer("ZRD")
            {
                Name = "PROJECTS",
                DataSource = dataSource,
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
                                BackColor = new Brush(_lblBackGroundColor),
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


        private Task AddLayerCnty()
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
                                Fill = new Brush(Color.Transparent),
                                Outline = new Pen(Color.Black, 2),
                                Opacity = 0.3f
                            },
                            new LabelStyle
                            {
                                Enabled = true,
                                LabelColumn = "NAME",
                                BackColor = new Brush(Color.Transparent),
                                ForeColor = new Color(_txtLBLColor),
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
                                Fill = new Brush(_vecBNDYFill),
                                Outline = new Pen(Color.Black, 2),
                            },
                            new VectorStyle
                            {
                                Fill = new Brush(_vecBNDYFill),
                                Outline = new Pen(_vecBNDYColor, 7),
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


        private async Task ExportXStoShp()
        {
            try
            {
                if (string.IsNullOrEmpty(_pathHdfB)) return;
                if (string.IsNullOrEmpty(_pathXS)) return;
                if (File.Exists(_pathXS)) return;

                System.Diagnostics.Debug.WriteLine($"\n XS FILE DOES NOT EXIST...CREATING @ {_pathXS}\n");
                await ExporterCrossSection.ExportXSToShp(
                    projPath: Path.GetDirectoryName(_pathHdfB) ?? string.Empty,
                    hdfPath: _pathHdfB,
                    outputShpPath: _pathXS
                );
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("ExportXS cancelled.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ExportXS error: {ex.Message}");
            }
        }


        public async Task ExportBNDY()
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
            try
            {

                var settingsRepo = App.ServiceProvider.GetRequiredService<IUserSettingsRepo>();

                var settings = !string.IsNullOrEmpty(projPathOverride)
                    ? await settingsRepo.GetSettingsFresh()
                    : await settingsRepo.GetSettings();

                string projPath = !string.IsNullOrEmpty(projPathOverride)
                    ? projPathOverride
                    : settings.ProjPath;

                if (string.IsNullOrEmpty(projPath)) return;

                if (!settings.Projects.TryGetValue(projPath, out var projSettings))
                {
                    System.Diagnostics.Debug.WriteLine($"BuildPaths: no settings found for '{projPath}'.");
                    return;
                }

                string projName = Path.GetFileNameWithoutExtension(projPath);
                string tmpPath = Path.GetFullPath(Path.Combine(projPath, ".."));
                string spatialPath = Path.Combine(tmpPath, "Spatial");

                if (!Directory.Exists(spatialPath))
                    Directory.CreateDirectory(spatialPath);

                _pathXS = Path.Combine(spatialPath, "XS.shp");
                _pathBNDY = Path.Combine(spatialPath, "BNDY.shp");
                _pathHdfA = projSettings.HdfPathA;
                _pathHdfB = projSettings.HdfPathB;


                if (!settings.Projects.TryGetValue(projPath, out projSettings))
                {
                    System.Diagnostics.Debug.WriteLine($"BuildPaths: lost settings for '{projPath}' after HDF update.");
                    return;
                }

                _pathHMS = projSettings.HmsPath;


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
                        System.Diagnostics.Debug.WriteLine($"BuildPaths: auto-set HdfPathA={_pathHdfB}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("BuildPaths: no .pxx.hdf file found.");
                    }
                }

                if (string.IsNullOrEmpty(projSettings.HdfPathB) || !File.Exists(projSettings.HdfPathB))
                {
                    string projDir = Directory.Exists(settings.ProjPath)
                        ? settings.ProjPath
                        : Path.GetDirectoryName(settings.ProjPath) ?? string.Empty;

                    var hdfFiles = Directory.GetFiles(projDir, "*.hdf")
                        .Where(f => HdfFileRegex().IsMatch(Path.GetFileName(f)))
                        .ToList();

                    string? hdfPathB = hdfFiles
                        .FirstOrDefault(f => !f.Equals(_pathHdfB, StringComparison.OrdinalIgnoreCase));

                    if (!string.IsNullOrEmpty(hdfPathB))
                    {
                        settings.Projects[settings.ProjPath].HdfPathB = hdfPathB;
                        await settingsRepo.SaveSettings(settings);
                        System.Diagnostics.Debug.WriteLine($"BuildPaths: auto-set HdfPathB={hdfPathB}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("BuildPaths: no second .pxx.hdf file found.");
                    }
                }

                _pathHMS = projSettings.HmsPath;

                if (string.IsNullOrEmpty(_pathHMS))
                {
                    if (string.IsNullOrEmpty(_pathHdfB))
                    {
                        System.Diagnostics.Debug.WriteLine("BuildPaths: skipping HMS prompt, project not fully configured yet.");
                        return;
                    }

                    var result = MessageBox.Show(
                        "HMS path is not set. Would you like to select it now?",
                        "HMS Path Missing",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        var dialog = new Microsoft.Win32.OpenFileDialog
                        {
                            Title = "Select HMS Run File",
                            Filter = "HMS Run Files (*.run)|*.run|All Files (*.*)|*.*",
                            //InitialDirectory = Path.GetDirectoryName(projPath) ?? string.Empty
                            InitialDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projPath) ?? string.Empty, ".."))
                        };

                        if (dialog.ShowDialog() == true)
                        {
                            string selectedPath = dialog.FileName;
                            string projRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projPath) ?? string.Empty, ".."));

                            if (!selectedPath.StartsWith(projRoot, StringComparison.OrdinalIgnoreCase))
                            {
                                MessageBox.Show(
                                    $"Selected file must be within the project root folder:\n{projRoot}",
                                    "Invalid HMS Path",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Warning);
                                return;
                            }

                            _pathHMS = selectedPath;
                            settings.Projects[settings.ProjPath].HmsPath = _pathHMS;
                            await settingsRepo.SaveSettings(settings);
                            EventBus.PublishHmsPathChanged(_pathHMS);
                            EventBus.PublishRunPath(_pathHMS);
                        }
                    }
                    if (string.IsNullOrEmpty(_pathHMS)) return;
                }

                if (!string.IsNullOrEmpty(_pathHMS))
                    EventBus.PublishHmsPathChanged(_pathHMS);

                _pathSubBasins = FindShapefileByName("subbasin") ?? string.Empty;
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
