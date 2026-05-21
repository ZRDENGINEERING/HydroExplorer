using BruTile.Predefined;
using BruTile.Web;
using HydroExplorer.Helpers;
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
using System.IO;
using System.Windows.Controls;



namespace HydroExplorer.View
{
    public partial class MapView : UserControl
    {
        private readonly MapStateService _mapState;
        public readonly EditingWidget? _editingWidget;
        private readonly MapControl _mapControl;
        private readonly Map _map;

        private string? _pathCL = string.Empty;
        private string? _pathXS = string.Empty;
        private string? _pathHdfA = string.Empty;
        private string? _pathHdfB = string.Empty;
        private string? _pathBNDY = string.Empty;
        private string? _pathHMS = string.Empty;
        private string? _pathSubBasins = string.Empty;

        private static readonly Color _lblBackGroundColor = new(236, 210, 1, 150);

        private static readonly Color _vecCLColor = new(70, 0, 0, 255);
        private static readonly Color _vecBNDYColor = new(70, 138, 138, 255);
        private static readonly Color _vecBNDYFill = new(128, 128, 128, 0);

        private readonly SelectionViewModel _selectionVm;
        private readonly WritableLayer _highlightLayer;

        private static readonly Color _txtLBLColor = new(0, 0, 0, 155);

        private CancellationTokenSource? _resetMapCts;


        private string? ResolveHdfPath() =>
            !string.IsNullOrEmpty(_pathHdfB) && File.Exists(_pathHdfB) ? _pathHdfB :
            !string.IsNullOrEmpty(_pathHdfA) && File.Exists(_pathHdfA) ? _pathHdfA :
            null;



        public MapView()
        {
            InitializeComponent();

            _mapState = App.ServiceProvider.GetRequiredService<MapStateService>();
            _mapState.PathsReady += OnPathsReady;


            _mapControl = new MapControl();
            _map = new Map { CRS = "EPSG:3857" };

            LoggingWidget.ShowLoggingInMap = Mapsui.Widgets.ActiveMode.No;

            _mapControl.MapTapped += OnMapTapped;

            

            _mapControl.Map = _map;
            _map.Widgets.Clear();


            _highlightLayer = new WritableLayer
            {
                Name = "Highlight",
                Style = new VectorStyle
                {
                    Outline = new Pen(Color.Yellow, 2),
                    Fill = null
                }
            };
            _map.Layers.Add(_highlightLayer);


            _selectionVm = App.ServiceProvider.GetRequiredService<SelectionViewModel>();
            _selectionVm.PropertyChanged += OnSelectionChanged;
            Content = _mapControl;

            Loaded += async (s, e) => { await ResetMap(); };


            Unloaded += (s, e) => _mapState.PathsReady -= OnPathsReady;
        }


        private void OnPathsReady(ProjectPaths paths)
        {
            Dispatcher.InvokeAsync(async () =>
            {
                try { await ResetMap(paths); }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"MapView.OnPathsReady error: {ex.Message}");
                }
            });
        }




        public async Task ResetMap(ProjectPaths? paths = null)
        {
            // Use provided paths, or fall back to last known
            paths ??= _mapState.CurrentPaths;
            if (paths == null) return;

            _pathXS = paths.PathXS;
            _pathCL = paths.PathCL;
            _pathBNDY = paths.PathBNDY;
            _pathHdfA = paths.PathHdfA;
            _pathHdfB = paths.PathHdfB;
            _pathHMS = paths.PathHMS;
            _pathSubBasins = paths.PathSubBasins;
            

            _resetMapCts?.Cancel();
            _resetMapCts?.Dispose();
            _resetMapCts = new CancellationTokenSource();
            var token = _resetMapCts.Token;

            await Task.Delay(100, token);
            _map.Layers.Clear();


            var lyr_bing = new TileLayer(KnownTileSources.Create(KnownTileSource.BingHybrid));
            //lyr_bing.Opacity = 1;

            _map.Layers.Add(lyr_bing);


            try
            {
                await AddLayerShpTXCnty();
                await AddLayerShpBndy();
                
                await ExportShpXS();
                var plotVm = App.ServiceProvider.GetRequiredService<PlotViewModel>();
                if (plotVm.WselData == null) return;
                await AddLayerShpXS(plotVm.WselData);

                await ExportShpCL();
                await AddLayerShpCL();

                

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





        private async Task ExportShpXS()
        {
            try
            {
                string? hdfPath = ResolveHdfPath();

                if (string.IsNullOrEmpty(hdfPath)) return;
                if (string.IsNullOrEmpty(_pathXS)) return;
                if (File.Exists(_pathXS)) return;

                System.Diagnostics.Debug.WriteLine($"\n XS FILE DOES NOT EXIST...CREATING @ {_pathXS}\n");
                await ExporterCrossSection.ExportXSToShp(
                    projPath: Path.GetDirectoryName(hdfPath) ?? string.Empty,
                    hdfPath: hdfPath,
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




        private async Task ExportShpCL()
        {
            try
            {
                string? hdfPath = ResolveHdfPath();


                if (string.IsNullOrEmpty(hdfPath)) return;
                if (string.IsNullOrEmpty(_pathCL)) return;
                if (File.Exists(_pathCL)) return;

                System.Diagnostics.Debug.WriteLine($"\n CL FILE DOES NOT EXIST...CREATING @ {_pathCL}\n");
                await ExporterStream.ExportCLToShp(
                    projPath: Path.GetDirectoryName(hdfPath) ?? string.Empty,
                    hdfPath: hdfPath,
                    outputShpPath: _pathCL
                );
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("ExportCL cancelled.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ExportCL error: {ex.Message}");
            }
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
                System.Diagnostics.Debug.WriteLine("END UtilInfo!! \n");
            }
        }


        private async Task InitView()
        {
            if (!Path.Exists(_pathXS)) return;

            var shapeFileProvider = new ShapeFile(_pathXS, true);
            if (shapeFileProvider.GetExtent() is MRect extent)
            {
                double xx = (extent.MaxX + extent.MinX) / 2;
                double yy = (extent.MaxY + extent.MinY) / 2;

                var (x, y) = SphericalMercator.FromLonLat(xx, yy);
                _map.Navigator.CenterOn(x, y);

                _map.Navigator.ZoomTo(25);
            }
        }

        private static void UtilInfoTextBox(string infoText)
        {
            System.Diagnostics.Debug.WriteLine($"infoText: {infoText} \n");
        }


        private static void InitInfoWidgets(Map map)
        {
            //var infoLayer = map.Layers.OfType<MemoryLayer>().FirstOrDefault(l => l.infoLayer);
            //_targetLayer = map.Layers.FirstOrDefault(f => f.Name == "Layer 3") as WritableLayer;
            //_editingWidget = map.Widgets.OfType<EditingWidget>().Single();
            //map.Widgets.Add(new MapInfoWidget(map, [map.Layers.Last()]));

            map.Widgets.Add(new MapInfoWidget(map, map.Layers.FindLayer("XS")));

            //map.Widgets.Add(CreateSelectButton());

            map.Widgets.Add(new MouseCoordinatesWidget());


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
                                Outline = new Pen(_vecBNDYColor, 7),
                            },
                                    new VectorStyle
                            {
                                Fill = new Brush(_vecBNDYFill),
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
                {
                    staIdx = i + 1;
                    break;
                }
            }

            var factory = NetTopologySuite.Geometries.GeometryFactory.Default;

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

                var match = wselData?.FirstOrDefault(w => w.RiverSta?.Trim() == rs);
                var lineColor = match != null && match.DELTA > 0
                    ? new Color(220, 50, 50, 255)    // red  — delta positive
                    : new Color(50, 205, 50, 255);   // green — delta zero/negative

                feature.Styles.Add(new VectorStyle
                {
                    Line = new Pen(lineColor, 1),
                    Outline = new Pen(lineColor, 1),
                    Fill = null,
                    Opacity = 0.85f
                });

                // Add label
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


        private async Task AddLayerShpCL()
        {
            if (!Path.Exists(_pathCL)) return;

            //if (File.Exists(_pathBNDY) && File.Exists(_pathSubBasins))
            //{
            //    var newer = GetNewerFile(_pathBNDY, _pathSubBasins);
            //    if (newer?.FullName == _pathBNDY) return;
            //}

            var shapeFileProvider = new ShapeFile(_pathCL) { CRS = "EPSG:4326" };
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
                    Line = new Pen
                            {
                                Color = Color.Blue,
                                Width = 1.5f,
                                //PenStyle = PenStyle.Dash
                            }
                        }
                    }
                }
            };
            _map.Layers.Add(new RasterizingTileLayer(shapefileLayer));
        }



        private void AddLayerShpZRD()
        {
            string shapefilePathZRD = "Z:\\10 DEV\\hydroExplorer\\SHP\\TX_ZRD.shp";
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



        private static VectorStyle CreateThemeZones()
        {
            return new VectorStyle
            {
                Outline = new Pen(Color.Gray),
                Fill = new Brush(Color.Gray),
                Opacity = 0.15f
            };
        }

        private static async Task<WmsProvider> CreateWmsProviderAsync()
        {
            const string wmsUrl = "https://hazards.fema.gov/arcgis/services/public/NFHLWMS/MapServer/WMSServer?request=GetCapabilities&service=WMS";

            //const string wmsUrl = "https://hazards.fema.gov/arcgis/rest/services/public/NFHLWMS/MapServer/WMSServer";

            var wmsProvider = await WmsProvider.CreateAsync(wmsUrl);
            wmsProvider.ContinueOnError = true;
            wmsProvider.TimeOut = 20000;
            wmsProvider.CRS = "EPSG:3857";
            //wmsProvider.AddLayer("Flood Hazard Zones");
            wmsProvider.AddLayer("10");

            //foreach (var layerName in allLayerNames)
            //{
            //    wmsProvider.AddLayer(layerName);
            //}

            wmsProvider.SetImageFormat(wmsProvider.OutputFormats[1]);
            return wmsProvider;
        }

        private static List<string> GetAllLayerNames(Client.WmsServerLayer layer)
        {
            var layerNames = new List<string>();
            // The "Name" property is used in the request, not the "Title"
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

            var fullpath = Path.GetFullPath(match);

            return fullpath;
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
                    {
                        staIdx = i + 1;
                        break;
                    }
                }

                if (staIdx == -1) return;

                while (reader.Read())
                {
                    var val = reader.GetString(staIdx)?.Trim();
                    if (val == riverSta?.Trim())
                    {

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


                            var highlightColor = delta > 0
                            ? new Color(0, 0, 255, 255)
                            : new Color(0, 0, 255, 255);

                            //? new Color(50, 205, 50, 255)
                            //: new Color(220, 50, 50, 255);


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
                            System.Diagnostics.Debug.WriteLine($"Inner error: {innerEx.Message}");
                            System.Diagnostics.Debug.WriteLine($"Stack: {innerEx.StackTrace}");
                        }
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ZoomToStation error: {ex.Message}");
            }
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

    }
}
