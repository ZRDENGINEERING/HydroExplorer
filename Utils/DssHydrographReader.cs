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

        // ── Single-open convenience wrappers ────────────────────────────────
        // Kept for callers reading only one series. Prefer ReadByPartC(dss, ...)
        // below when reading several series from the same file in one operation.

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
        /// Opens its own snapshot + DssReader, serialized via DssGate, for a
        /// single Part C read. Prefer the (DssReader, partC, runName) overload
        /// when reading multiple series from the same file — that overload does
        /// NOT take the gate itself; the caller holds it for the whole batch
        /// (see TabChartViewModel.LoadDssDataAsync).
        /// </summary>
        public static List<HydrographRecord> ReadByPartC(
            string dssFilePath, string partC, string runName = "")
        {
            var results = new List<HydrographRecord>();
            if (!File.Exists(dssFilePath)) return results;

            string? tempPath = null;
            DssGate.Enter();
            try
            {
                tempPath = DssSnapshot.Create(dssFilePath);
                using var dss = new DssReader(tempPath);
                return ReadByPartC(dss, partC, runName);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DssHydrographReader.ReadByPartC({partC}) error: {ex.Message}");
                return results;
            }
            finally
            {
                DssSnapshot.Cleanup(tempPath);
                DssGate.Exit();
            }
        }

        // ── Multi-read entry point — call against an already-open DssReader ────
        // Does NOT take DssGate itself — caller must hold the gate for the full
        // batch (open through dispose). See TabChartViewModel.LoadDssDataAsync.

        public static List<HydrographRecord> ReadByPartC(DssReader dss, string partC, string runName = "")
        {
            var results = new List<HydrographRecord>();
            try
            {
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
