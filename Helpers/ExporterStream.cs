using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using PureHDF;



namespace HydroExplorer.Helpers
{
    internal class ExporterStream
    {
        private static readonly GeometryFactory GeomFactory = new();

        public static async Task ExportCLToShp(string projPath, string hdfPath, string outputShpPath)
        {
            using var file = H5File.OpenRead(hdfPath);


            // Dump top-level Geometry children
            var geomGroup = file.Group("/Geometry");
            foreach (var child in geomGroup.Children())
                System.Diagnostics.Debug.WriteLine($"Geometry child: '{child.Name}'");

            var clGroup = file.Group("/Geometry/River Centerlines");




            var clAttributes = clGroup.Dataset("Attributes").Read<Dictionary<string, object>[]>();

            if (clAttributes.Length > 0)
                foreach (var kvp in clAttributes[0])
                    System.Diagnostics.Debug.WriteLine($"CL Attr Key: '{kvp.Key}'");










            //double[] allPoints = clGroup.Dataset("Polyline Points").Read<double[]>();
            //int[] polyInfo = clGroup.Dataset("Polyline Info").Read<int[]>();

            double[] allPoints = clGroup.Dataset("Polyline Points").Read<double[]>();
            int[] polyInfo = clGroup.Dataset("Polyline Info").Read<int[]>();

            var attributes = clGroup.Dataset("Attributes").Read<Dictionary<string, object>[]>();

            System.Diagnostics.Debug.WriteLine($"allPoints.Length={allPoints.Length} polyInfo.Length={polyInfo.Length} attributes.Length={attributes.Length}");

            int lineCount = attributes.Length;

            for (int i = 0; i < lineCount; i++)
            {
                int start = polyInfo[i * 4];
                int count = polyInfo[i * 4 + 1];
                System.Diagnostics.Debug.WriteLine($"CL[{i}]: start={start} count={count} needsIndex={(start + count - 1) * 2 + 1} arrayLen={allPoints.Length}");
            }

            string srcWkt = await GISUtil.FetchHECWkt(projPath);
            if (string.IsNullOrEmpty(srcWkt))
            {
                System.Diagnostics.Debug.WriteLine("ExportCLToShp: No source WKT resolved, falling back to EPSG:2277.");
                srcWkt = GISUtil.FetchWkt(2277);
            }

            string tgtWkt = GISUtil.FetchWkt(4326);
            var transform = GISUtil.CreateTransformation(srcWkt, tgtWkt);
            var features = new List<IFeature>(lineCount);

            if (attributes.Length > 0)
                foreach (var kvp in attributes[0])
                    System.Diagnostics.Debug.WriteLine($"CL Attr Key: '{kvp.Key}' Value: '{kvp.Value}'");

            for (int i = 0; i < lineCount; i++)
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
                    { "River", attr["River Name"]?.ToString() ?? "" },
                    { "Reach", attr["Reach Name"]?.ToString() ?? "" }
                };

                features.Add(new Feature(GeomFactory.CreateLineString(coords), attrTable));
            }

            var header = ShapefileDataWriter.GetHeader(features[0], features.Count);
            var writer = new ShapefileDataWriter(outputShpPath, GeomFactory) { Header = header };
            writer.Write(features);

            GISUtil.WriteShpPrj(outputShpPath, GISUtil.TryGetEpsgFromWkt(tgtWkt));

            System.Diagnostics.Debug.WriteLine($"Exported {features.Count} centerlines → {outputShpPath}");
        }
    }
}