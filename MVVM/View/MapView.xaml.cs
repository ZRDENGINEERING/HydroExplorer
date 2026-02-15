using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Nts.Providers;
using Mapsui.Nts.Providers.Shapefile;
using Mapsui.Nts.Widgets;
using Mapsui.Projections;
using Mapsui.Providers;
using Mapsui.Providers.Wms;
using Mapsui.Styles;
using Mapsui.Styles.Thematics;
using Mapsui.Tiling.Layers;
using Mapsui.UI.Wpf;
using Mapsui.Widgets.ButtonWidgets;
using Mapsui.Widgets.InfoWidgets;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using System.Windows.Controls;
using System.Windows.Media.TextFormatting;
using System.Xml.Linq;



namespace HydroExplorer.MVVM.View
{
    public class LayerData
    {
        public bool IsMapInfoLayer { get; set; }
    }
    public partial class MapView : UserControl
    {
        //private const string _mapInfoLayerName = "_infoLayerName";
        private EditingWidget? _editingWidget;
        //private WritableLayer? _targetLayer;

        public MapView()
        {
            InitializeComponent();

            var mapControl = new Mapsui.UI.Wpf.MapControl();
            //mapControl.Map?.Layers.Add(Mapsui.Tiling.OpenStreetMap.CreateTileLayer());
            var map = new Map { CRS = "EPSG:3857" };

            //var source = KnownTileSources.Create(KnownTileSource.BingAerial);
            //TileLayer bingSat = new TileLayer(source);
            //bingSat.Name = "Bing Aerial";
            //map.Layers.Add(bingSat);

            map.Layers.Add(Mapsui.Tiling.OpenStreetMap.CreateTileLayer());
            mapControl.Map = map;

            //SATX
            //var (x, y) = SphericalMercator.FromLonLat(-98.4936, 29.4241);
            //TX
            var (x, y) = SphericalMercator.FromLonLat(-99.1440, 31.4928);
            map.Navigator.CenterOn(x, y);

            InitLayers(map);
            InitInfoWidgets(map);

            Content = mapControl;
            map.Navigator.ZoomTo(3000);

            map.Tapped += (s, e) =>
            { 
                // Animate to the new center:
                //e.Map.Navigator.CenterOn(e.WorldPosition, 500, Easing.CubicOut);
                
                UtilInfo(map);
                e.Handled = true;
            };
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
                System.Diagnostics.Debug.WriteLine($"RIVER: { valRiver }");

                string rawReach = valueList.Where(x => x.Contains("Reach")).ElementAt(0);
                string valReach = rawReach.Split(':').ElementAt(1);
                System.Diagnostics.Debug.WriteLine($"REACH: { valReach }");

                string rawRiverStati = valueList.Where(x => x.Contains("RiverStati")).ElementAt(0);
                string valRiverStati = rawRiverStati.Split(':').ElementAt(1);
                System.Diagnostics.Debug.WriteLine($"STA: { valRiverStati } \n");

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


        private void InitInfoWidgets(Map map)
        {
            //var infoLayer = map.Layers.OfType<MemoryLayer>().FirstOrDefault(l => l.infoLayer);
            //_targetLayer = map.Layers.FirstOrDefault(f => f.Name == "Layer 3") as WritableLayer;
            //_editingWidget = map.Widgets.OfType<EditingWidget>().Single();
            //map.Widgets.Add(new MapInfoWidget(map, [map.Layers.Last()]));

            map.Widgets.Add(new MapInfoWidget(map, map.Layers.FindLayer("XS")));

            //map.Widgets.Add(CreateSelectButton());

            map.Widgets.Add(new MouseCoordinatesWidget());

            LoggingWidget.ShowLoggingInMap = Mapsui.Widgets.ActiveMode.Yes;
        }


        private void InitLayers(Map map)
        {
            AddShapefileLayer(map);
            
            //AddShapefileLayerZRD_TX(map);

            AddShapefileLayerXS(map);
            AddShapefileLayerZRD(map);
        }


        //public static Map CreateMap()
        //{
        //    var map = new Map { CRS = "EPSG:3857" };
        //    map.Layers.Add(OpenStreetMap.CreateTileLayer());
        //    map.Widgets.Add(new ZoomInOutWidget { Margin = new MRect(20, 40) });
        //    map.Widgets.Add(CreateTextBox("Tap on the map to center on that location"));
        //    map.Tapped += (s, e) =>
        //    {
        //        // Animate to the new center.
        //        e.Map.Navigator.CenterOn(e.WorldPosition, 500, Easing.CubicOut);
        //        e.Handled = true;
        //    };
        //    return map;
        //}

        //private static IWidget CreateTextBox(string text) => new TextBoxWidget()
        //{
        //    Text = text,
        //    //VerticalAlignment = VerticalAlignment.Top,
        //    //HorizontalAlignment = HorizontalAlignment.Left,
        //    Margin = new MRect(10),
        //    Padding = new MRect(8),
        //    CornerRadius = 4,
        //    BackColor = new Color(108, 117, 125, 128),
        //    TextColor = Color.White,
        //};



        public async Task CreateLayerAsync(Mapsui.UI.Wpf.MapControl mapControl)
        {
            var layer = new ImageLayer("NOAA WMS")
            {
                DataSource = await CreateWmsProviderAsync(),
                Style = new RasterStyle()
            };
            mapControl.Map?.Layers.Add(layer);
            //map.Layers.Add(layer);
        }







        private static void AddShapefileLayer(Map map)
        {
            string shapefilePath = "C:/Temp/test_pgon.shp";
            var shapeFileProvider = new ShapeFile(shapefilePath, true) { CRS = "EPSG:4326" };
            var dataSource = new ProjectingProvider(shapeFileProvider) { CRS = "EPSG:3857" };
            var shapefileLayer = new Layer("Zones") 
            {
                DataSource = dataSource,
                Style = CreateThemeZones()
            };

            map.Layers.Add(shapefileLayer);

            //Assert.IsEquals(dataSource.CRS, "EPSG:4326");
            if (shapeFileProvider.GetExtent() is MRect extent)
            {
                //map.Home = n => n.NavigateTo(extent);
                //map.Navigator.ZoomTo(200);
                System.Diagnostics.Debug.WriteLine("EXTENT: ");
                System.Diagnostics.Debug.WriteLine(extent);
            }
        }


        private static void AddShapefileLayerZRD(Map map)
        {
            string shapefilePathZRD = "C:/Temp/ZRD_TX.shp";
            var shapeFileProvider = new ShapeFile(shapefilePathZRD, true) { CRS = "EPSG:4326" };
            var dataSource = new ProjectingProvider(shapeFileProvider) { CRS = "EPSG:3857" };
            var shapefileLayer = new Layer("ZRD")
            {
                Name = "CONFIG",
                DataSource = dataSource,
                Tag = new LayerData { IsMapInfoLayer = true },
                Style = CreateThemeZRD()
            };
            map.Layers.Add(shapefileLayer);
        }



        private static void AddShapefileLayerZRD_TX(Map map)
        {
            map.CRS = "EPSG:3857";
            string geojsonfilePathZRD = "C:/Temp/ZRD_TX.geojson";

            var geoJson = new GeoJsonProvider(geojsonfilePathZRD) { CRS = "EPSG:4326" };
            var dataSource = new ProjectingProvider(geoJson) { CRS = "EPSG:3857" };
            map.Layers.Add(new RasterizingTileLayer(CreateCityLayer(dataSource)));
        }

        private static void AddShapefileLayerXS(Map map)
        {
            string shapefilePathZRD = "C:/Temp/XS_TAN_WGS84.shp";
            var shapeFileProvider = new ShapeFile(shapefilePathZRD, true) { CRS = "EPSG:4326" };
            var dataSource = new ProjectingProvider(shapeFileProvider) { CRS = "EPSG:3857" };
            var shapefileLayer = new Layer("ZRD")
            {
                Name = "XS",
                DataSource = dataSource,
                Tag = new LayerData { IsMapInfoLayer = true },
                Style = CreateThemeXS()
            };
            map.Layers.Add(shapefileLayer);
        }











        private static ILayer CreateCityLayer(IProvider citySource)
        {
            return new Layer
            {
                Name = "Cities",
                DataSource = citySource,
                Style = CreateThemeZRD()
            };
        }

        private static SymbolStyle CreateThemeZRD()
        {
            return new SymbolStyle
            {
                Fill = new Brush(Color.Red),
                SymbolScale = 0.2,
                Opacity = 1
            };
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

        private static VectorStyle CreateThemeXS()
        {
            return new VectorStyle
            {
                Outline = new Pen(Color.Magenta),
                Fill = new Brush(Color.Magenta),
                Opacity = 1f
            };
        }

        //private static string GetAppDir()
        //{
        //    return Path.GetDirectoryName(typeof(MapView).Assembly.Location);
        //}


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


        //private static async Task<WmsProvider> CreateWmsProviderAsync()
        //{
        //    const string wmsUrl = "https://mapservices.weather.noaa.gov/eventdriven/services/radar/radar_base_reflectivity_time/ImageServer/WMSServer";

        //    var provider = await WmsProvider.CreateAsync(wmsUrl);
        //    provider.ContinueOnError = true;
        //    provider.TimeOut = 20000;
        //    provider.CRS = "EPSG:84";
        //    provider.AddLayer("0");
        //    provider.SetImageFormat(provider.OutputFormats[1]);
        //    return provider;
        //}


        //private string ToString(IFeature feature)
        //{
        //    var result = new StringBuilder();
        //    if (feature == null || feature.Fields == null)
        //        return result.ToString();

        //    foreach (var field in feature.Fields)
        //    {
        //        result.Append($"{field}={feature[field]}, ");
        //    }

        //    result.Append($"Geometry={feature.Geometry}");
        //    return result.ToString();
        //}















        private ButtonWidget CreateSelectButton() => new()
        {
            Position = new MPoint(5, 320),
            Height = 18,
            Width = 120,
            CornerRadius = 2,
            //HorizontalAlignment = HorizontalAlignment.Absolute,
            //VerticalAlignment = VerticalAlignment.Absolute,
            Text = "Select (for delete)",
            BackColor = Color.LightGray,
            WithTappedEvent = (_, e) =>
            {
                _editingWidget!.SelectMode = !_editingWidget.SelectMode;
                e.Handled = true;
                System.Diagnostics.Debug.WriteLine("CreateSelectButton");
            }
        };






        // The edit layer has two styles. That is why it needs to use a StyleCollection.
        // In a future version of Mapsui the ILayer will have a Styles collections just
        // as the GeometryFeature has right now.
        // The first style is the basic style of the features in edit mode.
        // The second style is the way to show a feature is selected.
        private static StyleCollection CreateEditLayerStyle() => new()
        {
            Styles =
        {
            CreateEditLayerBasicStyle(),
            CreateSelectedStyle(),
            CreateStyleToShowTheVertices(),
        }
        };

        private static SymbolStyle CreateStyleToShowTheVertices() => new()
        {
            Outline = new Pen(Color.Gray, 1f),
            Fill = new Brush(Color.White),
            SymbolScale = 0.5
        };

        private static VectorStyle CreateEditLayerBasicStyle() => new()
        {
            Fill = new Brush(_editModeColor),
            Line = new Pen(_editModeColor, 3),
            Outline = new Pen(_editModeColor, 3)
        };

        private static readonly Color _editModeColor = new(124, 22, 111, 180);
        private static readonly Color _pointLayerColor = new(240, 240, 240, 240);
        private static readonly Color _lineLayerColor = new(150, 150, 150, 240);
        private static readonly Color _polygonLayerColor = new(20, 20, 20, 240);

        private static readonly SymbolStyle? _selectedStyle = new()
        {
            Fill = null,
            Outline = new Pen(Color.Red, 3),
            Line = new Pen(Color.Red, 3)
        };

        private static readonly SymbolStyle? _disableStyle = new() { Enabled = false };

        // To show the selected style a ThemeStyle is used which switches on and off the SelectedStyle
        // depending on a "Selected" attribute.
        private static ThemeStyle CreateSelectedStyle()
            => new(f => (bool?)f["Selected"] == true ? _selectedStyle : _disableStyle);

        private static WritableLayer CreatePointLayer() => new()
        {
            Name = "Layer 1",
            Style = CreatePointStyle()
        };

        private static WritableLayer CreateLineLayer()
        {
            var lineLayer = new WritableLayer
            {
                Name = "Layer 2",
                Style = CreateLineStyle()
            };

            // todo: add data

            return lineLayer;
        }

        private static WritableLayer CreatePolygonLayer()
        {
            var polygonLayer = new WritableLayer
            {
                Name = "Layer 3",
                Style = CreatePolygonStyle()
            };

            var wkt = "POLYGON ((1261416.17275404 5360656.05714234, 1261367.50386493 5360614.2556425, 1261353.47050427 5360599.62511755, 1261338.83997932 5360576.03712836, 1261337.34706862 5360570.6626498, 1261375.8641649 5360511.2448036, 1261383.92588273 5360483.17808227, 1261391.98760055 5360485.56673941, 1261393.48051126 5360480.490843, 1261411.99260405 5360487.6568144, 1261430.50469684 5360496.9128608, 1261450.21111819 5360507.06465361, 1261472.00761454 5360525.5767464, 1261488.13105019 5360544.98458561, 1261488.1310502 5360545.28316775, 1261481.26366093 5360549.76189988, 1261489.6239609 5360560.21227484, 1261495.59560374 5360555.13637843, 1261512.91336796 5360573.05130694, 1261535.00844645 5360598.43078898, 1261540.08434286 5360619.03295677, 1261535.90419287 5360621.12303176, 1261526.64814648 5360623.21310675, 1261489.32537876 5360644.41243881, 1261458.27283602 5360661.73020303, 1261438.26783253 5360662.02878517, 1261427.22029328 5360660.23729232, 1261416.17275404 5360656.05714234))";
            var polygon = new WKTReader().Read(wkt);
            IFeature feature = new GeometryFeature { Geometry = polygon };
            polygonLayer.Add(feature);

            return polygonLayer;
        }

        private static VectorStyle CreatePointStyle()
        {
            return new VectorStyle
            {
                Fill = new Brush(_pointLayerColor),
                Line = new Pen(_pointLayerColor, 3),
                Outline = new Pen(Color.Gray, 2)
            };
        }

        private static VectorStyle CreateLineStyle()
        {
            return new VectorStyle
            {
                Fill = new Brush(_lineLayerColor),
                Line = new Pen(_lineLayerColor, 3),
                Outline = new Pen(_lineLayerColor, 3)
            };
        }

        private static VectorStyle CreatePolygonStyle()
        {
            return new VectorStyle
            {
                Fill = new Brush(new Color(_polygonLayerColor)),
                Line = new Pen(_polygonLayerColor, 3),
                Outline = new Pen(_polygonLayerColor, 3)
            };
        }

        private static MRect? GetGrownExtent(ILayer? layer) => layer?.Extent?.Grow(layer.Extent.Width * 0.2) ?? null;



        public class LayerData
        {
            public bool IsMapInfoLayer { get; set; }
        }




    }
}
