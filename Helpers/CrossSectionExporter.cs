using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;
using PureHDF;
using System.IO;
using System.Net.Http;


namespace HydroExplorer.Helpers
{
    public class CrossSectionExporter
    {
        private static readonly GeometryFactory GeomFactory = new();

        static readonly HttpClient client = new();
        private string _srcWkt = string.Empty;
        private string _tgtWkt = string.Empty;


        public async void ExportToShapefile(string projPath, string hdfPath, string outputShp)
        {
            //if (!Directory.Exists(outputShp)) return;

            using var file = H5File.OpenRead(hdfPath);
            var xsGroup = file.Group("/Geometry/Cross Sections");

            // Size: 8 (double), Dims: [326, 2]
            double[] allPoints = xsGroup.Dataset("Polyline Points").Read<double[]>();

            // Dims: [83, 4] — columns are: startIndex, pointCount, ?, ?
            int[] polyInfo = xsGroup.Dataset("Polyline Info").Read<int[]>();
            //for (int i = 0; i < 3; i++)
            //System.Diagnostics.Debug.WriteLine($"XS {i}: col0={polyInfo[i * 4]} col1={polyInfo[i * 4 + 1]} col2={polyInfo[i * 4 + 2]} col3={polyInfo[i * 4 + 3]}");

            var attributes = xsGroup.Dataset("Attributes")
                .Read<Dictionary<string, object>[]>();

            // Print all keys from first record
            //foreach (var kvp in attributes[0])
            //    System.Diagnostics.Debug.WriteLine($"Key: '{kvp.Key}' Value: '{kvp.Value}' Type: {kvp.Value?.GetType().Name}");

            await FetchHECRASWkt(projPath);

            int tgtEPSG = 4326;
            //string sourceWkt = FetchWkt(srcEPSG);
            await FetchWkt(tgtEPSG);

            var transform = ReprojectionHelper.CreateTransformation(_srcWkt, _tgtWkt);


            int xsCount = attributes.Length;
            var features = new List<IFeature>(xsCount);

            for (int i = 0; i < xsCount; i++)
            {
                int start = polyInfo[i * 4];      // col 0: start point index
                int count = polyInfo[i * 4 + 1];  // col 1: point count

                var coords = new Coordinate[count];
                for (int j = 0; j < count; j++)
                {
                    var raw = new Coordinate(
                        allPoints[(start + j) * 2],
                        allPoints[(start + j) * 2 + 1]
                    );
                    coords[j] = ReprojectionHelper.Reproject(raw, transform);  // <-- reproject here
                }

                var attr = attributes[i];
                var attrTable = new AttributesTable
        {
            { "River",   attr["River"]?.ToString()   ?? "" },
            { "Reach",   attr["Reach"]?.ToString()   ?? "" },
            { "RS", Convert.ToDouble(attr["RS"]) }
        };

                features.Add(new Feature(GeomFactory.CreateLineString(coords), attrTable));
            }

            var header = ShapefileDataWriter.GetHeader(features[0], features.Count);
            var writer = new ShapefileDataWriter(outputShp, GeomFactory) { Header = header };
            writer.Write(features);

            await ProjectionHelper.WritePrjAsync(outputShp, tgtEPSG);

            System.Diagnostics.Debug.WriteLine($"Exported {features.Count} cross sections → {outputShp}");
        }


        public async Task FetchWkt(int epsgCode)
        {

            //string  strReq = ($"https://epsg.io/{epsgCode}.wkt").ToString();
            //return client.GetStringAsync(strReq);


            try
            {
                _tgtWkt = await client.GetStringAsync($"https://epsg.io/{epsgCode}.wkt");
            }
            catch (HttpRequestException e)
            {
                System.Diagnostics.Debug.WriteLine("\nException Caught!");
                System.Diagnostics.Debug.WriteLine("Message :{0} ", e.Message);
            }
        }



        public async Task FetchHECRASWkt(string projPath)
        {
            string tmpPath = Path.GetFullPath(Path.Combine(projPath, ".."));
            string projPrjPath = Path.Combine(tmpPath, "prj");

            string firstFilePath = Directory.EnumerateFiles(projPrjPath).FirstOrDefault();

            if (firstFilePath != null)
            {
                string projPrjName = Path.GetFileNameWithoutExtension(firstFilePath);
                //await FetchWkt(int.Parse(projPrjName));

                _srcWkt = await client.GetStringAsync($"https://epsg.io/{int.Parse(projPrjName)}.wkt");
            }
            else
            {
                return;
            }
        }




        public static class ProjectionHelper
        {
            public static async Task WritePrjAsync(string shpPath, int epsgCode)
            {
                using var http = new HttpClient();
                string wkt = await http.GetStringAsync($"https://epsg.io/{epsgCode}.wkt");
                File.WriteAllText(Path.ChangeExtension(shpPath, ".prj"), wkt);
            }
        }

        public static class ReprojectionHelper
        {
            public static ICoordinateTransformation CreateTransformation(string sourceWkt, string targetWkt)
            {
                var factory = new CoordinateSystemFactory();
                var source = factory.CreateFromWkt(sourceWkt);
                var target = factory.CreateFromWkt(targetWkt);
                return new CoordinateTransformationFactory().CreateFromCoordinateSystems(source, target);
            }

            public static Coordinate Reproject(Coordinate coord, ICoordinateTransformation transform)
            {
                double[] result = transform.MathTransform.Transform([coord.X, coord.Y]);
                return new Coordinate(result[0], result[1]);
            }
        }

    }
}