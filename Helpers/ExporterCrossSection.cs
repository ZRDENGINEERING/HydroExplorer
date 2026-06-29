using HydroExplorer.Utils;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using PureHDF;
using PureHDF.VOL.Native;
using System.IO;



namespace HydroExplorer.Helpers
{
    public class ExporterCrossSection
    {
        private static readonly GeometryFactory GeomFactory = new();

        /// <summary>
        /// Builds XS.shp from a HEC-RAS plan/geometry HDF's cross-section polylines.
        /// Source CRS resolution: sourceEpsgOverride (if supplied) takes priority,
        /// otherwise resolved from the HEC-RAS project's own .prj folder. If neither
        /// is available, returns false rather than silently guessing — callers should
        /// resolve a CRS first (e.g. GISUtil.GuessTexasStatePlaneZone with user
        /// confirmation) and pass it in, since the wrong zone misplaces results badly.
        /// </summary>
        public static async Task<bool> ExportXSToShp(string projPath, string hdfPath, string outputShpPath, int? sourceEpsgOverride = null)
        {
            if (string.IsNullOrEmpty(hdfPath) || !File.Exists(hdfPath))
            {
                System.Diagnostics.Debug.WriteLine($"ExportXSToShp: HDF Path missing or not found: '{hdfPath}'.");
                return false;
            }

            using var file = HecRasHdfReader.OpenHdf(hdfPath);

            // Guard: 2D models / plans without 1D cross-section geometry won't have
            // this group at all — fail gracefully rather than throwing unhandled.
            if (!HasCrossSectionGeometry(file))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"ExportXSToShp: '{hdfPath}' has no 1D Cross Sections geometry, skipping.");
                return false;
            }

            var xsGroup = file.Group("/Geometry/Cross Sections");

            double[] allPoints = xsGroup.Dataset("Polyline Points").Read<double[]>();
            int[] polyInfo = xsGroup.Dataset("Polyline Info").Read<int[]>();
            var attributes = xsGroup.Dataset("Attributes").Read<Dictionary<string, object>[]>();

            string srcWkt;
            if (sourceEpsgOverride.HasValue)
            {
                srcWkt = GISUtil.FetchWkt(sourceEpsgOverride.Value);
                System.Diagnostics.Debug.WriteLine(
                    $"ExportXSToShp: using explicit source override EPSG={sourceEpsgOverride.Value}");
            }
            else
            {
                srcWkt = await GISUtil.FetchHECWkt(projPath);
                if (string.IsNullOrEmpty(srcWkt))
                {
                    System.Diagnostics.Debug.WriteLine(
                        "ExportXSToShp: no .prj folder found for HEC-RAS project and no override supplied.");
                    return false;
                }
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

            if (features.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine($"ExportXSToShp: no cross sections found in '{hdfPath}'.");
                return false;
            }

            var header = ShapefileDataWriter.GetHeader(features[0], features.Count);
            var writer = new ShapefileDataWriter(outputShpPath, GeomFactory) { Header = header };
            writer.Write(features);

            GISUtil.WriteShpPrj(outputShpPath, GISUtil.TryGetEpsgFromWkt(tgtWkt));

            System.Diagnostics.Debug.WriteLine($"Exported {features.Count} cross sections → {outputShpPath}");
            return true;
        }

        private static bool HasCrossSectionGeometry(NativeFile file)
        {
            try
            {
                _ = file.Dataset("/Geometry/Cross Sections/Polyline Points");
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}