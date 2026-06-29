using Hec.Dss;
using System.IO;

namespace HydroExplorer.Utils
{
    /// <summary>
    /// Reads HMS hydrograph results (FLOW, FLOW-BASE, FLOW-DIRECT, FLOW-UNIT GRAPH,
    /// FLOW-CUMULATIVE, ELEVATION) from a DSS-7 file.
    /// </summary>
    public class DssHydrographReader
    {
        public record HydrographRecord(DateTime Time, double Value);

        public static List<HydrographRecord> ReadFlow(string dssFilePath, string runName = "")
            => ReadByPartC(dssFilePath, "FLOW", runName);

        public static List<HydrographRecord> ReadFlowBase(string dssFilePath, string runName = "")
            => ReadByPartC(dssFilePath, "FLOW-BASE", runName);

        public static List<HydrographRecord> ReadFlowDirect(string dssFilePath, string runName = "")
            => ReadByPartC(dssFilePath, "FLOW-DIRECT", runName);

        public static List<HydrographRecord> ReadFlowUnitGraph(string dssFilePath, string runName = "")
            => ReadByPartC(dssFilePath, "FLOW-UNIT GRAPH", runName);

        public static List<HydrographRecord> ReadFlowCumulative(string dssFilePath, string runName = "")
            => ReadByPartC(dssFilePath, "FLOW-CUMULATIVE", runName);

        public static List<HydrographRecord> ReadElevation(string dssFilePath, string runName = "")
            => ReadByPartC(dssFilePath, "ELEVATION", runName);

        /// <summary>
        /// Generic read by Part C (parameter type). Matches run name against Part F,
        /// handling both plain run names and HEC-HMS "RUN:" prefix.
        /// </summary>
        public static List<HydrographRecord> ReadByPartC(
            string dssFilePath, string partC, string runName = "")
        {
            var results = new List<HydrographRecord>();
            if (!File.Exists(dssFilePath)) return results;

            try
            {
                using var dss = new DssReader(dssFilePath);
                var catalog = dss.GetCatalog();

                var paths = catalog
                    .Where(p => p.FullPath.Contains($"/{partC}/",
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (paths.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"DssHydrographReader: no paths for partC='{partC}'");
                    return results;
                }

                var selected = MatchRun(paths, runName);
                //System.Diagnostics.Debug.WriteLine($"DssHydrographReader.ReadByPartC({partC}): {selected.FullPath}");

                return ReadTimeSeries(dss, selected.FullPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DssHydrographReader.ReadByPartC({partC}) error: {ex.Message}");
                return results;
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

        private static List<HydrographRecord> ReadTimeSeries(DssReader dss, string fullPath)
        {
            var results = new List<HydrographRecord>();
            var ts = dss.GetTimeSeries(new DssPath(fullPath));
            if (ts == null || ts.Count == 0) return results;
            for (int i = 0; i < ts.Count; i++)
            {
                var v = ts.Values[i];
                if (double.IsNaN(v) || v <= -900) continue;
                results.Add(new HydrographRecord(ts.Times[i], v));
            }
            return results;
        }
    }
}
