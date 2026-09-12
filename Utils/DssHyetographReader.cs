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

        /// <summary>
        /// Opens its own snapshot + DssReader, serialized via DssGate. Prefer
        /// the (DssReader, runName) overload when reading multiple series from
        /// the same file — that overload does NOT take the gate itself; caller
        /// holds it for the whole batch.
        /// </summary>
        public static List<HyetographRecord> ReadPrecipInc(
            string dssFilePath, string preferredRun = "")
        {
            var results = new List<HyetographRecord>();
            if (!File.Exists(dssFilePath)) return results;

            string? tempPath = null;
            DssGate.Enter();
            try
            {
                tempPath = DssSnapshot.Create(dssFilePath);
                using var dss = new DssReader(tempPath);
                return ReadPrecipInc(dss, preferredRun);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"DssHyetographReader.ReadPrecipInc error: {ex.Message}");
                return results;
            }
            finally
            {
                DssSnapshot.Cleanup(tempPath);
                DssGate.Exit();
            }
        }

        /// <summary>
        /// Reads PRECIP-INC against an already-open DssReader. Does NOT take
        /// DssGate — caller must hold it for the batch.
        /// </summary>
        public static List<HyetographRecord> ReadPrecipInc(
            DssReader dss, string preferredRun = "",
            IEnumerable<DssPath>? catalog = null)
        {
            var results = new List<HyetographRecord>();
            try
            {
                var precipPaths = (catalog ?? dss.GetCatalog())
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
        /// Returns all DSS pathnames — opens its own snapshot + DssReader,
        /// serialized via DssGate. Prefer the (DssReader) overload if a reader
        /// is already open for this file.
        /// </summary>
        public static List<string> GetAllPaths(string dssFilePath)
        {
            if (!File.Exists(dssFilePath)) return [];

            string? tempPath = null;
            DssGate.Enter();
            try
            {
                tempPath = DssSnapshot.Create(dssFilePath);
                using var dss = new DssReader(tempPath);
                return GetAllPaths(dss);
            }
            catch (Exception ex)
            {
                if (ex is IOException)
                    System.Diagnostics.Debug.WriteLine(
                        "DSS file may be locked by HEC-HMS");
                return [];
            }
            finally
            {
                DssSnapshot.Cleanup(tempPath);
                DssGate.Exit();
            }
        }

        /// <summary>
        /// Returns all DSS pathnames against an already-open DssReader. Does
        /// NOT take DssGate — caller must hold it for the batch.
        /// </summary>
        public static List<string> GetAllPaths(DssReader dss)
        {
            try
            {
                var catalog = dss.GetCatalog().ToList();
                return catalog.Select(p => p.FullPath).ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetAllPaths error: {ex.Message}");
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
