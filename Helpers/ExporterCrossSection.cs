using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using PureHDF;
using System.IO;



namespace HydroExplorer.Helpers
{
    public class ExporterCrossSection
    {
        private static readonly GeometryFactory GeomFactory = new();

        public static async Task ExportXSToShp(string projPath, string hdfPath, string outputShpPath)
        {
            if (string.IsNullOrEmpty(hdfPath) || !File.Exists(hdfPath))
            {
                System.Diagnostics.Debug.WriteLine($"ExportXSToShp: HDF Path missing or not found: '{hdfPath}'.");
                return;
            }

            using var file = H5File.OpenRead(hdfPath);
            var xsGroup = file.Group("/Geometry/Cross Sections");

            double[] allPoints = xsGroup.Dataset("Polyline Points").Read<double[]>();
            int[] polyInfo = xsGroup.Dataset("Polyline Info").Read<int[]>();
            var attributes = xsGroup.Dataset("Attributes").Read<Dictionary<string, object>[]>();

            string srcWkt = await GISUtil.FetchHECWkt(projPath);
            if (string.IsNullOrEmpty(srcWkt))
            {
                System.Diagnostics.Debug.WriteLine("ExportXSToShp: No source WKT resolved, falling back to EPSG:2277.");
                srcWkt = GISUtil.FetchWkt(2277);
            }

            string tgtWkt = GISUtil.FetchWkt(4326);
            var transform = GISUtil.CreateTransformation(srcWkt, tgtWkt);

            int xsCount = attributes.Length;
            var features = new List<IFeature>(xsCount);

            for (int i = 0; i < xsCount; i++)
            {
                int start = polyInfo[i * 4];
                int count = polyInfo[i * 4 + 1];

                var coords = new Coordinate[count];
                for (int j = 0; j < count; j++)
                {
                    var raw = new Coordinate(
                        allPoints[(start + j) * 2],
                        allPoints[(start + j) * 2 + 1]
                    );
                    coords[j] = GISUtil.Reproject(raw, transform);
                }

                var attr = attributes[i];
                var attrTable = new AttributesTable
                {
                    { "River", attr["River"]?.ToString() ?? "" },
                    { "Reach", attr["Reach"]?.ToString() ?? "" },
                    { "RS",    Convert.ToDouble(attr["RS"]) }
                };

                features.Add(new Feature(GeomFactory.CreateLineString(coords), attrTable));
            }

            var header = ShapefileDataWriter.GetHeader(features[0], features.Count);
            var writer = new ShapefileDataWriter(outputShpPath, GeomFactory) { Header = header };
            writer.Write(features);

            GISUtil.WriteShpPrj(outputShpPath, GISUtil.TryGetEpsgFromWkt(tgtWkt));

            System.Diagnostics.Debug.WriteLine($"Exported {features.Count} cross sections → {outputShpPath}");
        }
    }
}