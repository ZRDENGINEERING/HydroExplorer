using PureHDF;
using PureHDF.VOL.Native;
using System.IO;

namespace HydroExplorer.Utils
{
    // ── Result containers ────────────────────────────────────────────────────

    public class HecRas2DCell
    {
        public string AreaName { get; set; } = string.Empty;
        public int CellIndex { get; set; }
        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public double MaxWSE { get; set; }
        public double MaxDepth { get; set; }
        public double MaxVelX { get; set; }  // face-averaged, X component
        public double MaxVelY { get; set; }  // face-averaged, Y component
        public double MaxVelMag { get; set; }  // magnitude
        public double MinTerrain { get; set; }  // cell minimum terrain elevation
    }

    public class HecRas2DProfilePoint
    {
        public string AreaName { get; set; } = string.Empty;
        public string ProfileName { get; set; } = string.Empty;
        public int PointIndex { get; set; }
        public double Station { get; set; }  // cumulative distance along line (ft or m)
        public double WSE { get; set; }
        public double Terrain { get; set; }
    }

    public class HecRas2DResults
    {
        public List<HecRas2DCell> Cells { get; set; } = [];
        public List<HecRas2DProfilePoint> ProfileLines { get; set; } = [];
        public string? TerrainPath { get; set; }
    }

    // ── Reader ───────────────────────────────────────────────────────────────

    public static class HecRas2DHdfReader
    {
        // Unsteady max-result paths
        private const string UnsteadyBase = "/Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/2D Flow Areas/";
        private const string SteadyBase = "/Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/2D Flow Areas/";
        private const string GeomBase = "/Geometry/2D Flow Areas/";

        // ── Public entry point ───────────────────────────────────────────────

        /// <summary>
        /// Reads 2D results from a plan HDF (*.p##.hdf) and its paired geometry
        /// HDF (*.g##.hdf).  Pass null for planPath to get geometry-only output.
        /// </summary>
        public static HecRas2DResults Read(string geomHdfPath, string? planHdfPath)
        {
            var results = new HecRas2DResults();

            if (!File.Exists(geomHdfPath)) return results;

            using var geomFile = OpenHdf(geomHdfPath);

            // Terrain path — stored as an attribute on the root or geometry group
            results.TerrainPath = ReadTerrainPath(geomFile, geomHdfPath);

            // 2D area names from geometry
            var areaNames = Read2DAreaNames(geomFile);
            if (areaNames.Count == 0) return results;

            // Determine which result block to use
            bool hasUnsteady = false;
            bool hasSteady = false;
            NativeFile? planFile = null;

            if (!string.IsNullOrEmpty(planHdfPath) && File.Exists(planHdfPath))
            {
                planFile = OpenHdf(planHdfPath);
                hasUnsteady = HasGroup(planFile, "/Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/2D Flow Areas");
                hasSteady = HasGroup(planFile, "/Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/2D Flow Areas");
            }

            try
            {
                foreach (var areaName in areaNames)
                {
                    var cells = ReadCells(geomFile, planFile, areaName, hasUnsteady, hasSteady);
                    results.Cells.AddRange(cells);

                    if (planFile != null)
                    {
                        var profilePoints = ReadProfileLines(geomFile, planFile, areaName, hasUnsteady, hasSteady);
                        results.ProfileLines.AddRange(profilePoints);
                    }
                }
            }
            finally
            {
                planFile?.Dispose();
            }

            return results;
        }

        // ── Cell centers + max results ───────────────────────────────────────

        private static List<HecRas2DCell> ReadCells(
            NativeFile geomFile, NativeFile? planFile,
            string areaName, bool hasUnsteady, bool hasSteady)
        {
            // Cell center coordinates — always in geometry HDF
            // Shape: [nCells, 2]  col 0 = X, col 1 = Y
            var centerPath = $"{GeomBase}{areaName}/Cells Center Coordinate";
            if (!HasDataset(geomFile, centerPath)) return [];

            float[,]? centers = TryReadCoordinatePairs(geomFile, centerPath);
            if (centers == null) return [];
            int nCells = centers.GetLength(0);

            // Minimum terrain per cell
            var terrainPath = $"{GeomBase}{areaName}/Cells Minimum Elevation";
            float[]? terrain = HasDataset(geomFile, terrainPath) ? TryReadFloat1D(geomFile, terrainPath) : null;

            // Max results — only if plan file available and has results
            float[]? maxWSE = null;
            float[]? maxDepth = null;
            float[]? maxVelX = null;
            float[]? maxVelY = null;
            float[]? maxVelMag = null;

            if (planFile != null)
            {
                string resultBase = hasUnsteady
                    ? $"{UnsteadyBase}{areaName}/"
                    : hasSteady
                        ? $"{SteadyBase}{areaName}/"
                        : null!;

                if (resultBase != null)
                {
                    maxWSE = TryReadFloat1D(planFile, resultBase + "Maximum Water Surface");
                    maxDepth = TryReadFloat1D(planFile, resultBase + "Maximum Depth");
                    maxVelX = TryReadFloat1D(planFile, resultBase + "Maximum Velocity - Cell X");
                    maxVelY = TryReadFloat1D(planFile, resultBase + "Maximum Velocity - Cell Y");
                    maxVelMag = TryReadFloat1D(planFile, resultBase + "Maximum Velocity");
                }
            }

            var cells = new List<HecRas2DCell>(nCells);
            for (int i = 0; i < nCells; i++)
            {
                cells.Add(new HecRas2DCell
                {
                    AreaName = areaName,
                    CellIndex = i,
                    CenterX = Math.Round(centers[i, 0], 4),
                    CenterY = Math.Round(centers[i, 1], 4),
                    MinTerrain = terrain != null && i < terrain.Length ? Math.Round(terrain[i], 2) : double.NaN,
                    MaxWSE = maxWSE != null && i < maxWSE.Length ? Math.Round(maxWSE[i], 2) : double.NaN,
                    MaxDepth = maxDepth != null && i < maxDepth.Length ? Math.Round(maxDepth[i], 2) : double.NaN,
                    MaxVelX = maxVelX != null && i < maxVelX.Length ? Math.Round(maxVelX[i], 3) : double.NaN,
                    MaxVelY = maxVelY != null && i < maxVelY.Length ? Math.Round(maxVelY[i], 3) : double.NaN,
                    MaxVelMag = maxVelMag != null && i < maxVelMag.Length ? Math.Round(maxVelMag[i], 3) : double.NaN,
                });
            }

            return cells;
        }

        // ── Profile lines ────────────────────────────────────────────────────

        private static List<HecRas2DProfilePoint> ReadProfileLines(
            NativeFile geomFile, NativeFile planFile,
            string areaName, bool hasUnsteady, bool hasSteady)
        {
            // Profile lines live under geometry: /Geometry/2D Flow Areas/{area}/Profile Lines/
            var profileNamesPath = $"{GeomBase}{areaName}/Profile Lines/Attributes";
            if (!HasDataset(geomFile, profileNamesPath)) return [];

            string[] profileNames;
            try
            {
                profileNames = geomFile.Dataset(profileNamesPath).Read<string[]>();
            }
            catch
            {
                return [];
            }

            string resultBase = hasUnsteady
                ? $"{UnsteadyBase}{areaName}/Profile Lines/"
                : hasSteady
                    ? $"{SteadyBase}{areaName}/Profile Lines/"
                    : null!;

            if (resultBase == null) return [];

            var output = new List<HecRas2DProfilePoint>();

            for (int p = 0; p < profileNames.Length; p++)
            {
                string profileName = profileNames[p].Trim();

                // Stations (cumulative distance) along profile line
                // Shape: [nPoints] — stored in geometry
                var stationPath = $"{GeomBase}{areaName}/Profile Lines/{profileName}/Stations";
                var terrainPath = $"{GeomBase}{areaName}/Profile Lines/{profileName}/Terrain";
                var wsePath = resultBase + $"{profileName}/Water Surface";

                float[]? stations = TryReadFloat1D(geomFile, stationPath);
                float[]? terrain = TryReadFloat1D(geomFile, terrainPath);
                float[]? wse = TryReadFloat1D(planFile, wsePath);

                if (stations == null) continue;

                int nPoints = stations.Length;

                for (int i = 0; i < nPoints; i++)
                {
                    output.Add(new HecRas2DProfilePoint
                    {
                        AreaName = areaName,
                        ProfileName = profileName,
                        PointIndex = i,
                        Station = Math.Round(stations[i], 2),
                        Terrain = terrain != null && i < terrain.Length ? Math.Round(terrain[i], 2) : double.NaN,
                        WSE = wse != null && i < wse.Length ? Math.Round(wse[i], 2) : double.NaN,
                    });
                }
            }

            return output;
        }

        // ── Terrain path ─────────────────────────────────────────────────────

        private static string? ReadTerrainPath(NativeFile geomFile, string geomHdfPath)
        {
            // RAS stores terrain path as an attribute on /Geometry or root
            foreach (var attrPath in new[] { "/Geometry", "/" })
            {
                try
                {
                    var group = geomFile.Group(attrPath);
                    foreach (var attr in group.Attributes())
                    {
                        if (attr.Name.Contains("Terrain", StringComparison.OrdinalIgnoreCase) ||
                            attr.Name.Contains("DEM", StringComparison.OrdinalIgnoreCase))
                        {
                            string raw = attr.Read<string>().Trim();
                            return ResolveTerrainPath(raw, geomHdfPath);
                        }
                    }
                }
                catch { }
            }
            return null;
        }

        /// <summary>
        /// RAS terrain paths are often stored relative to the project folder.
        /// Resolve against the geometry HDF's directory; return absolute path if
        /// the file exists, otherwise return the raw stored string for the caller.
        /// </summary>
        private static string ResolveTerrainPath(string raw, string geomHdfPath)
        {
            if (Path.IsPathRooted(raw) && File.Exists(raw))
                return raw;

            string folder = Path.GetDirectoryName(geomHdfPath) ?? string.Empty;
            string resolved = Path.GetFullPath(Path.Combine(folder, raw));

            return File.Exists(resolved) ? resolved : raw;
        }

        // ── Projection ───────────────────────────────────────────────────────

        /// <summary>
        /// Attempts to read the project's coordinate system as a WKT string, stored
        /// by RAS as an attribute on the geometry HDF's root or /Geometry group
        /// (name varies by RAS version — "Projection", "Coordinate System", etc.).
        /// Returns null if no such attribute is present; callers should fall back
        /// to a coordinate-based guess (see GISUtil.GuessTexasStatePlaneZone).
        /// </summary>
        public static string? TryReadProjectionWkt(string geomHdfPath)
        {
            if (!File.Exists(geomHdfPath)) return null;

            try
            {
                using var geomFile = OpenHdf(geomHdfPath);

                foreach (var groupPath in new[] { "/Geometry", "/" })
                {
                    try
                    {
                        var group = geomFile.Group(groupPath);
                        foreach (var attr in group.Attributes())
                        {
                            if (attr.Name.Contains("Projection", StringComparison.OrdinalIgnoreCase) ||
                                attr.Name.Contains("Coordinate System", StringComparison.OrdinalIgnoreCase) ||
                                attr.Name.Contains("WKT", StringComparison.OrdinalIgnoreCase))
                            {
                                string raw = attr.Read<string>()?.Trim() ?? string.Empty;
                                if (!string.IsNullOrEmpty(raw)) return raw;
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return null;
        }

        // ── 2D area names ────────────────────────────────────────────────────

        private static List<string> Read2DAreaNames(NativeFile geomFile)
        {
            var names = new List<string>();
            try
            {
                var group = geomFile.Group("/Geometry/2D Flow Areas");
                foreach (var link in group.Children())
                {
                    if (link is IH5Group)
                        names.Add(link.Name);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Read2DAreaNames: failed to enumerate 2D flow areas — {ex}");
            }
            return names;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static float[]? TryReadFloat1D(NativeFile file, string path)
        {
            try { return file.Dataset(path).Read<float[]>(); }
            catch
            {
                // RAS HDF5 output is usually float32, but isn't guaranteed to be —
                // fall back to double before giving up, converting element-wise.
                try
                {
                    var d = file.Dataset(path).Read<double[]>();
                    var f = new float[d.Length];
                    for (int i = 0; i < d.Length; i++) f[i] = (float)d[i];
                    return f;
                }
                catch { return null; }
            }
        }

        /// <summary>
        /// Reads an [n, 2] coordinate dataset (e.g. "Cells Center Coordinate"),
        /// trying float32 first and falling back to float64 — same reasoning as
        /// TryReadFloat1D. Returns null if neither read succeeds.
        /// </summary>
        private static float[,]? TryReadCoordinatePairs(NativeFile file, string path)
        {
            try { return file.Dataset(path).Read<float[,]>(); }
            catch
            {
                try
                {
                    var d = file.Dataset(path).Read<double[,]>();
                    var f = new float[d.GetLength(0), d.GetLength(1)];
                    for (int i = 0; i < d.GetLength(0); i++)
                        for (int j = 0; j < d.GetLength(1); j++)
                            f[i, j] = (float)d[i, j];
                    return f;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"TryReadCoordinatePairs: both float and double reads failed for '{path}': {ex}");
                    return null;
                }
            }
        }

        private static bool HasDataset(NativeFile file, string path)
        {
            try { _ = file.Dataset(path); return true; }
            catch { return false; }
        }

        private static bool HasGroup(NativeFile file, string path)
        {
            try { _ = file.Group(path); return true; }
            catch { return false; }
        }

        internal static NativeFile OpenHdf(string path)
        {
            using var fs = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096);
            // Load into MemoryStream so the FileStream can be released — same
            // pattern as HecRasHdfReader.OpenHdf, avoids holding the file lock
            // for the lifetime of the NativeFile.
            var ms = new MemoryStream((int)fs.Length);
            fs.CopyTo(ms);
            ms.Position = 0;
            return H5File.Open(ms);
        }
    }
}