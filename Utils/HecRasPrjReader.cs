using PureHDF;
using System.IO;
using System.Text.RegularExpressions;


namespace HydroExplorer.Utils
{
    public static class HecRasPrjReader
    {
        public static string? GetGeomExtForPlan(string planHdfPath)
        {
            string dir = Path.GetDirectoryName(planHdfPath) ?? string.Empty;

            string? prjPath = Directory.GetFiles(dir, "*.prj", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(IsHecRasProjectFile);

            if (prjPath == null) return null;

            var planMatch = Regex.Match(
                Path.GetFileName(planHdfPath),
                @"\.p(\d+)\.hdf$",
                RegexOptions.IgnoreCase);

            if (!planMatch.Success) return null;
            string planExt = $"p{planMatch.Groups[1].Value}";

            string baseName = Path.GetFileNameWithoutExtension(prjPath);

            foreach (var line in File.ReadAllLines(prjPath))
            {
                if (ParseValue(line, "Plan File") is string val &&
                    val.Equals(planExt, StringComparison.OrdinalIgnoreCase))
                {
                    string planFilePath = Path.Combine(dir, $"{baseName}.{planExt}");
                    if (!File.Exists(planFilePath)) return null;

                    foreach (var planLine in File.ReadAllLines(planFilePath))
                    {
                        if (ParseValue(planLine, "Geom File") is string geomExt)
                            return geomExt;
                    }

                    return null;
                }
            }

            return null;
        }

        /// <summary>
        /// Returns the resolved *.g##.hdf path paired to the given plan HDF,
        /// or null if not found.
        /// </summary>
        public static string? ResolveGeomHdf(string planHdfPath)
        {
            string dir = Path.GetDirectoryName(planHdfPath) ?? string.Empty;

            string? prjPath = Directory.GetFiles(dir, "*.prj", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(IsHecRasProjectFile);

            if (prjPath == null) return null;

            string? geomExt = GetGeomExtForPlan(planHdfPath);
            if (geomExt == null) return null;

            string baseName = Path.GetFileNameWithoutExtension(prjPath);
            string geomHdf = Path.Combine(dir, $"{baseName}.{geomExt}.hdf");

            return File.Exists(geomHdf) ? geomHdf : null;
        }

        private static string? ParseValue(string line, string key)
        {
            int eq = line.IndexOf('=');
            if (eq < 1) return null;
            if (!line[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) return null;
            return line[(eq + 1)..].Trim();
        }

        public static bool IsHecRasProjectFile(string filePath)
        {
            try
            {
                using var reader = new StreamReader(filePath);
                return reader.ReadLine()?.Trim()
                    .StartsWith("Proj Title", StringComparison.OrdinalIgnoreCase) == true;
            }
            catch { return false; }
        }
    }

    /// <summary>
    /// Scans a HEC-RAS project folder to determine model dimensionality
    /// by inspecting geometry HDF files — without loading them fully into memory.
    /// 2D models always have at least one *.g##.hdf; 1D-only projects may have none.
    /// </summary>
    public static class HecRasProjectScanner
    {
        // Matches RAS geometry HDF files: ProjectName.g01.hdf, .g02.hdf, etc.
        private const string GeomHdfPattern = "*.g??.hdf";

        /// <summary>
        /// Returns the model dimensionality for the given project folder.
        /// Exits early as soon as a 2D mesh is confirmed in any geometry HDF.
        /// </summary>
        public static ModelDimensions ScanProjectFolder(string projectFolder)
        {
            if (string.IsNullOrEmpty(projectFolder) || !Directory.Exists(projectFolder))
                return ModelDimensions.Unknown;

            var hdfFiles = Directory.GetFiles(projectFolder, GeomHdfPattern,
                                              SearchOption.TopDirectoryOnly);

            if (hdfFiles.Length == 0)
                return ModelDimensions.OneDOnly; // no HDFs → 1D-only project

            bool has1D = false;
            bool has2D = false;

            foreach (var hdfPath in hdfFiles)
            {
                var (file1D, file2D) = PeekModelDimensions(hdfPath);
                if (file1D) has1D = true;
                if (file2D) has2D = true;

                if (has2D) break; // early exit — enough to know it's 2D
            }

            return (has1D, has2D) switch
            {
                (true, true) => ModelDimensions.Mixed,
                (false, true) => ModelDimensions.TwoDOnly,
                (true, false) => ModelDimensions.OneDOnly,
                _ => ModelDimensions.Unknown
            };
        }

        /// <summary>
        /// Opens the HDF via a seekable FileStream (no ReadAllBytes) so PureHDF
        /// only reads the superblock and group B-tree nodes needed to resolve the
        /// two geometry group paths — mesh data is never loaded.
        /// </summary>
        private static (bool has1D, bool has2D) PeekModelDimensions(string hdfPath)
        {
            try
            {
                // FileStream — seekable, shared read, no full load into memory.
                // PureHDF will seek to the HDF5 superblock and traverse only the
                // object header / B-tree pages required to resolve each group path.
                using var fs = new FileStream(
                    hdfPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 4096);

                using var file = H5File.Open(fs);

                bool has1D = false;
                bool has2D = false;

                try { has1D = file.Group("/Geometry/Cross Sections").Children().Any(); }
                catch { }

                try { has2D = file.Group("/Geometry/2D Flow Areas").Children().Any(); }
                catch { }

                return (has1D, has2D);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"PeekModelDimensions: failed on '{hdfPath}' — {ex.Message}");
                return (false, false);
            }
        }
    }

    public enum ModelDimensions
    {
        Unknown,
        OneDOnly,
        TwoDOnly,
        Mixed   // 1D reaches + 2D areas in same project
    }
}