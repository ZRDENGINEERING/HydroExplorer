using HydroExplorer.Utils;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using PureHDF;
using PureHDF.VOL.Native;
using System.IO;



namespace HydroExplorer.Helpers
{
    internal class ExporterRiver
    {
        private static readonly GeometryFactory GeomFactory = new();

        /// <summary>
        /// Builds a centerline shapefile from a HEC-RAS plan/geometry HDF's river
        /// centerline polylines.
        /// Source CRS resolution: sourceEpsgOverride (if supplied) takes priority,
        /// otherwise resolved from the HEC-RAS project's own .prj folder. If neither
        /// is available, returns false rather than silently guessing — callers should
        /// resolve a CRS first (e.g. GISUtil.GuessTexasStatePlaneZone with user
        /// confirmation) and pass it in, since the wrong zone misplaces results badly.
        /// </summary>
        public static async Task<bool> ExportRiverToShp(string projPath, string hdfPath, string outputShpPath, int? sourceEpsgOverride = null)
        {
            if (string.IsNullOrEmpty(hdfPath) || !File.Exists(hdfPath))
            {
                System.Diagnostics.Debug.WriteLine($"ExportCLToShp: HDF Path missing or not found: '{hdfPath}'.");
                return false;
            }

            using var file = HecRasHdfReader.OpenHdf(hdfPath);

            // Guard: 2D models / plans without a River Centerlines group won't have
            // this group at all — fail gracefully rather than throwing unhandled.
            if (!HasRiverCenterlines(file))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"ExportCLToShp: '{hdfPath}' has no River Centerlines geometry, skipping.");
                return false;
            }

            var clGroup = file.Group("/Geometry/River Centerlines");

            double[] allPoints = clGroup.Dataset("Polyline Points").Read<double[]>();
            int[] polyInfo = clGroup.Dataset("Polyline Info").Read<int[]>();
            var attributes = clGroup.Dataset("Attributes").Read<Dictionary<string, object>[]>();

            string srcWkt;
            if (sourceEpsgOverride.HasValue)
            {
                srcWkt = GISUtil.FetchWkt(sourceEpsgOverride.Value);
                //System.Diagnostics.Debug.WriteLine(
                //    $"ExportCLToShp: using explicit source override EPSG={sourceEpsgOverride.Value}");
            }
            else
            {
                srcWkt = await GISUtil.FetchHECWkt(projPath);
                if (string.IsNullOrEmpty(srcWkt))
                {
                    //System.Diagnostics.Debug.WriteLine(
                    //    "ExportCLToShp: no .prj folder found for HEC-RAS project and no override supplied.");
                    return false;
                }
            }

            string tgtWkt = GISUtil.FetchWkt(4326);
            var transform = GISUtil.CreateTransformation(srcWkt, tgtWkt);

            int lineCount = attributes.Length;
            var features = new List<IFeature>(lineCount);

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

            if (features.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine($"ExportCLToShp: no centerlines found in '{hdfPath}'.");
                return false;
            }

            var header = ShapefileDataWriter.GetHeader(features[0], features.Count);
            var writer = new ShapefileDataWriter(outputShpPath, GeomFactory) { Header = header };
            writer.Write(features);

            GISUtil.WriteShpPrj(outputShpPath, GISUtil.TryGetEpsgFromWkt(tgtWkt));

            System.Diagnostics.Debug.WriteLine($"Exported {features.Count} centerlines → {outputShpPath}");
            return true;
        }

        private static bool HasRiverCenterlines(NativeFile file)
        {
            try
            {
                _ = file.Dataset("/Geometry/River Centerlines/Polyline Points");
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}