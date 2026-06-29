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

        /// <summary>
        /// Texas's 5 State Plane (NAD83, US survey feet) zones, with their documented
        /// EPSG area-of-use bounding boxes in WGS84 lon/lat. Sourced from the EPSG
        /// registry (epsg.io). Used as a last-resort guess when a source shapefile has
        /// no .prj sidecar at all — never silently assume a single zone (e.g. 2277),
        /// since picking the wrong adjacent zone can shift a boundary by tens or
        /// hundreds of miles (e.g. 2277 vs 2278 lands well outside Texas).
        /// </summary>
        private static readonly (int Epsg, string Name, double MinLon, double MinLat, double MaxLon, double MaxLat)[] TexasStatePlaneZones =
        [
            (2275, "Texas North",         -103.05, 34.56, -100.00, 36.50),
            (2276, "Texas North Central", -103.07, 31.72,  -94.00, 34.58),
            (2277, "Texas Central",       -106.66, 29.78,  -93.50, 32.27),
            (2278, "Texas South Central", -105.00, 27.78,  -93.76, 30.67),
            (2279, "Texas South",          -99.90, 25.84,  -96.90, 28.40),
        ];

        /// <summary>
        /// Guesses which Texas State Plane zone a projected point likely belongs to,
        /// by transforming it (under each candidate zone's own definition) to WGS84
        /// and checking which result actually falls within that zone's documented
        /// area of use. Returns -1 if no zone matches (point likely isn't in Texas,
        /// or isn't in any State Plane projection at all).
        /// Intended as a starting suggestion for user confirmation, not a silent
        /// auto-pick — zone boundaries can be ambiguous near shared edges.
        /// </summary>
        public static int GuessTexasStatePlaneZone(Coordinate projectedPoint)
        {
            foreach (var zone in TexasStatePlaneZones)
            {
                try
                {
                    string srcWkt = FetchWkt(zone.Epsg);
                    if (string.IsNullOrEmpty(srcWkt)) continue;

                    string tgtWkt = FetchWkt(4326);
                    var transform = CreateTransformation(srcWkt, tgtWkt);
                    var result = Reproject(projectedPoint, transform);

                    if (result.X >= zone.MinLon && result.X <= zone.MaxLon &&
                        result.Y >= zone.MinLat && result.Y <= zone.MaxLat)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"GuessTexasStatePlaneZone: matched EPSG:{zone.Epsg} ({zone.Name}).");
                        return zone.Epsg;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"GuessTexasStatePlaneZone: EPSG:{zone.Epsg} check failed — {ex.Message}");
                }
            }

            System.Diagnostics.Debug.WriteLine("GuessTexasStatePlaneZone: no zone matched.");
            return -1;
        }

        /// <summary>
        /// Convenience overload: guesses the zone from a shapefile's extent centroid.
        /// </summary>
        public static int GuessTexasStatePlaneZone(Envelope extent)
        {
            var centroid = new Coordinate(
                (extent.MinX + extent.MaxX) / 2,
                (extent.MinY + extent.MaxY) / 2);
            return GuessTexasStatePlaneZone(centroid);
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