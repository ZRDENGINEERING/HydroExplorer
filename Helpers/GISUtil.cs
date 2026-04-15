using NetTopologySuite.Geometries;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;



namespace HydroExplorer.Helpers
{
    public static partial class GISUtil
    {
        private static readonly GeometryFactory GeomFactory = new();

        private static readonly HttpClient _client = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        private static readonly Dictionary<int, string> _wktCache = [];

        public static string FetchWkt(int epsgCode)
        {
            if (_wktCache.TryGetValue(epsgCode, out string? cached))
                return cached;

            try
            {
                string wkt = _client.GetStringAsync($"https://epsg.io/{epsgCode}.wkt")
                                    .GetAwaiter()
                                    .GetResult();
                _wktCache[epsgCode] = wkt;
                return wkt;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FetchWkt failed for EPSG:{epsgCode} — {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Reads projection from HEC-RAS prj subfolder.
        /// Filename is the EPSG code (e.g. 2277.prj) — fetches authoritative WKT from epsg.io.
        /// Falls back to reading file contents as raw WKT if filename is not numeric.
        /// </summary>
        public static async Task<string> FetchHECWkt(string projPath)
        {
            if (string.IsNullOrEmpty(projPath)) return string.Empty;

            string dir = Directory.Exists(projPath)
                ? projPath
                : Path.GetDirectoryName(projPath) ?? string.Empty;

            if (string.IsNullOrEmpty(dir)) return string.Empty;

            string prjFolder = Path.Combine(dir, "prj");

            if (!Directory.Exists(prjFolder))
            {
                System.Diagnostics.Debug.WriteLine($"FetchHECWkt: prj folder not found at '{prjFolder}'.");
                return string.Empty;
            }

            string? prjFile = Directory.EnumerateFiles(prjFolder, "*.prj").FirstOrDefault();

            if (prjFile == null)
            {
                System.Diagnostics.Debug.WriteLine($"FetchHECWkt: no .prj file found in '{prjFolder}'.");
                return string.Empty;
            }

            string stem = Path.GetFileNameWithoutExtension(prjFile);

            if (int.TryParse(stem, out int epsg))
            {
                // Use cache if available
                if (_wktCache.TryGetValue(epsg, out string? cached))
                {
                    System.Diagnostics.Debug.WriteLine($"FetchHECWkt: EPSG:{epsg} from cache.");
                    return cached;
                }

                string wkt = await _client.GetStringAsync($"https://epsg.io/{epsg}.wkt");
                _wktCache[epsg] = wkt;
                System.Diagnostics.Debug.WriteLine($"FetchHECWkt: loaded EPSG:{epsg} from '{prjFile}'.");
                return wkt;
            }

            string content = await File.ReadAllTextAsync(prjFile);
            if (content.TrimStart().StartsWith("PROJCS", StringComparison.OrdinalIgnoreCase) ||
                content.TrimStart().StartsWith("GEOGCS", StringComparison.OrdinalIgnoreCase))
            {
                System.Diagnostics.Debug.WriteLine($"FetchHECWkt: loaded raw WKT from '{prjFile}'.");
                return content;
            }

            System.Diagnostics.Debug.WriteLine($"FetchHECWkt: '{stem}' is not a valid EPSG code and file is not WKT.");
            return string.Empty;
        }

        /// <summary>
        /// Writes EPSG-named .prj file into HEC-RAS prj subfolder.
        /// Clears any existing .prj files first to avoid stale EPSG.
        /// </summary>
        public static void WritePrjToHECRAS(string projPath, int epsgCode)
        {
            string dir = Directory.Exists(projPath)
                ? projPath
                : Path.GetDirectoryName(projPath) ?? string.Empty;

            string prjFolder = Path.Combine(dir, "prj");
            if (!Directory.Exists(prjFolder))
                Directory.CreateDirectory(prjFolder);

            foreach (var old in Directory.EnumerateFiles(prjFolder, "*.prj"))
                File.Delete(old);

            string wkt = FetchWkt(epsgCode);
            string prjFile = Path.Combine(prjFolder, $"{epsgCode}.prj");
            File.WriteAllText(prjFile, wkt);
            System.Diagnostics.Debug.WriteLine($"WritePrjToHECRAS: wrote EPSG:{epsgCode} to '{prjFile}'.");
        }

        /// <summary>
        /// Writes WKT as a sidecar .prj alongside a shapefile.
        /// </summary>
        public static void WriteShpPrj(string shpPath, int epsgCode)
        {
            string wkt = FetchWkt(epsgCode);
            string prjFile = Path.ChangeExtension(shpPath, ".prj");
            File.WriteAllText(prjFile, wkt);
            System.Diagnostics.Debug.WriteLine($"WriteShpPrj: wrote EPSG:{epsgCode} to '{prjFile}'.");
        }

        public static int TryGetEpsgFromWkt(string wkt)
        {
            var match = EpsgAuthorityRegex().Match(wkt);
            return match.Success && int.TryParse(match.Groups[1].Value, out int epsg) ? epsg : -1;
        }

        public static ICoordinateTransformation CreateTransformation(string sourceWkt, string targetWkt)
        {
            var factory = new CoordinateSystemFactory();
            var source = factory.CreateFromWkt(sourceWkt);
            var target = factory.CreateFromWkt(targetWkt);
            return new CoordinateTransformationFactory().CreateFromCoordinateSystems(source, target);
        }

        public static Coordinate Reproject(Coordinate coord, ICoordinateTransformation transform)
        {
            try
            {
                double[] result = transform.MathTransform.Transform([coord.X, coord.Y]);

                if (double.IsNaN(result[0]) || double.IsNaN(result[1]))
                {
                    System.Diagnostics.Debug.WriteLine($"Reproject: NaN for X={coord.X}, Y={coord.Y}");
                    return coord;
                }

                return new Coordinate(result[0], result[1]);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Reproject failed: X={coord.X}, Y={coord.Y} — {ex.Message}");
                return coord;
            }
        }

        private static bool IsHecRasProjectFile(string filePath)
        {
            try
            {
                using StreamReader reader = new(filePath);
                var firstLine = reader.ReadLine()?.Trim();
                return firstLine?.StartsWith("Proj Title", StringComparison.OrdinalIgnoreCase) == true;
            }
            catch { return false; }
        }


        public static void DeleteShapefileIfExists(string shpPath)
        {
            string[] extensions = { ".shp", ".shx", ".dbf", ".prj", ".cpg", ".sbn", ".sbx" };
            string basePath = Path.ChangeExtension(shpPath, null);

            foreach (var ext in extensions)
            {
                try
                {
                    string file = basePath + ext;
                    if (File.Exists(file))
                        File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    System.Diagnostics.Debug.WriteLine($"[WARN] Could not delete {basePath}{ext}: {ex.Message}");
                }
            }
        }


        [GeneratedRegex(@"AUTHORITY\[""EPSG""\s*,\s*""(\d+)""\]", RegexOptions.RightToLeft)]
        private static partial Regex EpsgAuthorityRegex();
    }
}