using Hec.Dss;
using System.IO;

namespace HydroExplorer.Utils
{
    /// <summary>
    /// Reads HMS hyetograph (precipitation) data from a DSS-7 file.
    /// For flow/hydrograph data use DssHydrographReader.
    /// </summary>
    public class DssHyetographReader
    {
        public record HyetographRecord(DateTime Time, double Value);

        public static List<HyetographRecord> ReadPrecipInc(
            string dssFilePath, string preferredRun = "")
        {
            var results = new List<HyetographRecord>();
            if (!File.Exists(dssFilePath)) return results;
            try
            {
                using var dss = new DssReader(dssFilePath);
                var catalog = dss.GetCatalog();

                var precipPaths = catalog
                    .Where(p => p.FullPath.Contains("PRECIP-INC",
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (!precipPaths.Any()) return results;

                var selected = MatchRun(precipPaths, preferredRun);
                System.Diagnostics.Debug.WriteLine(
                    $"DssHyetographReader.ReadPrecipInc: {selected.FullPath}");

                var ts = dss.GetTimeSeries(new DssPath(selected.FullPath));
                if (ts == null || ts.Count == 0) return results;

                for (int i = 0; i < ts.Count; i++)
                {
                    var v = ts.Values[i];
                    if (double.IsNaN(v) || v <= -900) continue;
                    results.Add(new HyetographRecord(ts.Times[i], v));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"DssHyetographReader.ReadPrecipInc error: {ex.Message}");
            }
            return results;
        }

        /// <summary>
        /// Returns all DSS pathnames in the file — used to enumerate run names.
        /// </summary>
        public static List<string> GetAllPaths(string dssFilePath)
        {
            if (!File.Exists(dssFilePath)) return [];
            try
            {
                using var dss = new DssReader(dssFilePath);
                var catalog = dss.GetCatalog().ToList();
                //System.Diagnostics.Debug.WriteLine(
                //    $"GetAllPaths: count={catalog.Count} file={dssFilePath}");
                //foreach (var p in catalog.Take(5))
                    //System.Diagnostics.Debug.WriteLine($"  SAMPLE: '{p.FullPath}'");
                return catalog.Select(p => p.FullPath).ToList();
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"GetAllPaths error: {ex.Message}");
                if (ex is IOException)
                    System.Diagnostics.Debug.WriteLine(
                        "DSS file may be locked by HEC-HMS");
                return [];
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static DssPath MatchRun(List<DssPath> paths, string runName)
        {
            if (string.IsNullOrEmpty(runName)) return paths.First();
            return paths.FirstOrDefault(p =>
                       p.FullPath.Contains($"RUN:{runName}",
                           StringComparison.OrdinalIgnoreCase) ||
                       p.FullPath.Contains(runName,
                           StringComparison.OrdinalIgnoreCase))
                   ?? paths.First();
        }
    }
}
