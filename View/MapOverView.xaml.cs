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

        private readonly string _pathTMP = @"C:\\Temp\\tmp.shp";
        private string _pathXS;
        private string _pathBNDY;
        private string _pathHdfA;
        private string _pathHMS = string.Empty;
        private string _pathSubBasins;

        private static readonly Color _lblBackGroundColor = new(236, 210, 1, 150);
        private static readonly Color _vecBNDYColor = new(70, 138, 138, 155);
        private static readonly Color _vecBNDYFill = new(128, 128, 128, 50);
        private static readonly Color _txtLBLColor = new(0, 0, 0, 155);



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

            var lyr_osm = Mapsui.Tiling.OpenStreetMap.CreateTileLayer();
            lyr_osm.Opacity = 0.6;
            _map.Layers.Add(lyr_osm);

            _mapControl.Map = _map;

            //SATX
            //var (x, y) = SphericalMercator.FromLonLat(-98.4936, 29.4241);
            //TX
            //var (x, y) = SphericalMercator.FromLonLat(-99.1440, 31.4928);
            //map.Navigator.CenterOn(x, y);

            //InitLayers(map);
            //AddLayerShpTxPgon();

            //var shpPath = "Z:\\10 DEV\\hydroExplorer\\SHP\\TX_CNTY.shp";
            //_map.Layers.Add(CreateProjectedShapefileLayer(shpPath));


            Content = _mapControl;

            Loaded += async (s, e) =>
            {
                //await ExportXS();
                await BuildPaths();
                //await ExportBNDY();
                await AddLayerShpBndy();
                await AddLayerCnty();
                //await AddLayerShpXS();
                await InitView();
                //AddLayerShpZRD();
            };

            //InitInfoWidgets();
            //AddLayerShpZRD();

            EventBus.ProjPathChanged += async path =>
            {
                await Dispatcher.InvokeAsync(async () =>
                {
                    await ResetMap();
                });
            };

            //_map.Tapped += (s, e) =>
            //{
            //    // Animate to the new center:
            //    //e.Map.Navigator.CenterOn(e.WorldPosition, 500, Easing.CubicOut);
            //    UtilInfo(_map);
            //    e.Handled = true;
            //};
        }


        public async Task ResetMap()
        {
            _map.Layers.Clear();

            var lyr_osm = Mapsui.Tiling.OpenStreetMap.CreateTileLayer();
            lyr_osm.Opacity = 0.4;
            _map.Layers.Add(lyr_osm);

            await ExportXS();
            await BuildPaths();
            await ExportBNDY();

            await AddLayerShpBndy();
            await AddLayerCnty();

            //await AddLayerShpXS();
            //AddLayerShpZRD();

            await InitView();

            //InitInfoWidgets();
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
                List<string> valueList = infoText.Split(sep).ToList();

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
            var shapeFileProvider = new ShapeFile(shapefilePath, true) { CRS = "EPSG:4326" };
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
            var provider = new ShapeFile(shapefilePath, true)
            {
                CRS = "EPSG:4326"  // Set the source CRS of your shapefile
            };

            // Wrap in a ProjectingProvider to reproject to Web Mercator (3857)
            var projectingProvider = new ProjectingProvider(provider)
            {
                CRS = "EPSG:3857"
            };

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






        private async Task AddLayerCnty()
        {
            string shapefilePathCnty = "Z:\\10 DEV\\hydroExplorer\\SHP\\TX_CNTY.shp";

            if (!Path.Exists(shapefilePathCnty)) return;
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
        }


        private async Task AddLayerShpXS()
        {
            if (!Path.Exists(_pathXS)) return;
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


        private async Task ExportXS()
        {
            var settingsRepo = App.ServiceProvider.GetRequiredService<IUserSettingsRepo>();
            var settings = await settingsRepo.GetSettings();

            string projPath = settings.ProjPath;

            //if (!string.IsNullOrEmpty(projPath) && settings.Projects.TryGetValue(projPath, out var projSettings))
            if (!string.IsNullOrEmpty(projPath) && settings.Projects.TryGetValue(projPath, out var projSettings))
            {
                var _projects = settings.Projects[projPath];

                string hdfPathA = _projects.HdfPathA;

                string tmpPath = Path.GetFullPath(Path.Combine(projPath, ".."));

                string spatialPath = Path.Combine(tmpPath, "Spatial");

                if (!Directory.Exists(spatialPath))
                {
                    Directory.CreateDirectory(spatialPath);
                }

                _pathXS = Path.Combine(spatialPath, "XS.shp");

                if (!File.Exists(_pathXS))
                {
                    System.Diagnostics.Debug.WriteLine($"\n XS FILE DOES NOT EXIST...CREATING @ {_pathXS}\n");
                    CrossSectionExporter xsExp = new();
                    xsExp.ExportToShapefile(
                        projPath: projPath,
                        hdfPath: hdfPathA,
                        outputShp: _pathXS
                    );
                }
            }
        }


        public async Task ExportBNDY()
        {
            var settingsRepo = App.ServiceProvider.GetRequiredService<IUserSettingsRepo>();
            var settings = await settingsRepo.GetSettings();
            string projPath = settings.ProjPath;
            if (File.Exists(_pathBNDY) && File.Exists(_pathSubBasins))
            {
                string newer = GetNewerFile(_pathBNDY, _pathSubBasins).ToString();
                System.Diagnostics.Debug.WriteLine($"\n NEWER SUBBASINS...@ {newer}\n");

                if (newer == _pathBNDY) return;
            }
            else
                if (File.Exists(_pathSubBasins))
                {
                    BndyExporter expBNDY = new();
                    await expBNDY.GeoDissolve(
                        inputShp: _pathSubBasins,
                        outputShp: _pathTMP
                    );
                    await expBNDY.UtilReproject(
                        inputShp: _pathTMP,
                        outputShp: _pathBNDY
                    );
                }
        }


        public static FileInfo GetNewerFile(string pathA, string pathB)
        {
            if (pathA == "" && pathB == "") return null;

            var fileA = new FileInfo(pathA);
            var fileB = new FileInfo(pathB);

            if (!fileA.Exists) throw new FileNotFoundException($"File not found: {pathA}");
            if (!fileB.Exists) throw new FileNotFoundException($"File not found: {pathB}");

            return fileA.LastWriteTime > fileB.LastWriteTime ? fileA : fileB;
        }


        private async Task BuildPaths()
        {
            var settingsRepo = App.ServiceProvider.GetRequiredService<IUserSettingsRepo>();
            var settings = await settingsRepo.GetSettings();
            string projPath = settings.ProjPath;
            if (!string.IsNullOrEmpty(projPath) && settings.Projects.TryGetValue(projPath, out var projSettings))
            {
                var _projects = settings.Projects[projPath];

                string _pathHdfATmp = _projects.HdfPathA;

                string tmpPath = Path.GetFullPath(Path.Combine(projPath, ".."));

                string spatialPath = Path.Combine(tmpPath, "Spatial");

                if (!Directory.Exists(spatialPath))
                {
                    Directory.CreateDirectory(spatialPath);
                }

                _pathXS = Path.Combine(spatialPath, "XS.shp");
                _pathBNDY = Path.Combine(spatialPath, "BNDY.shp");
                _pathHdfA = _pathHdfATmp;
                _pathHMS = _projects.HmsPath;
                _pathSubBasins = FindShapefileByName("subbasin");
            }
        }


        private string FindShapefileByName(string searchText)
        {
            string mapsPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(_pathHMS), "maps"));

            if (!Directory.Exists(mapsPath))
            {
                System.Diagnostics.Debug.WriteLine($"MapOverView Maps folder not found: {mapsPath}");
                return null;
            }
            var match = Directory.GetFiles(mapsPath, "*.shp", SearchOption.AllDirectories)
                    .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f)
                        .Contains(searchText, StringComparison.OrdinalIgnoreCase));

            //System.Diagnostics.Debug.WriteLine(match != null
            //    ? $"Found shapefile: {match}"
            //    : $"No shapefile containing '{searchText}' found in {mapsPath}");

            var fullpath = Path.GetFullPath(match);

            return fullpath;
        }
    }
}
