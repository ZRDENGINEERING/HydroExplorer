using Hec.Dss;
using System.IO;


namespace HydroExplorer.Utils
{
    public class DssHyetographReader
    {
        public record HyetographRecord(DateTime Time, double Value);

        public static List<HyetographRecord> ReadPrecipInc(string dssFilePath, string preferredRun = "")
        {
            var results = new List<HyetographRecord>();

            if (!File.Exists(dssFilePath)) return results;

            try
            {
                using var dss = new DssReader(dssFilePath);
                var catalog = dss.GetCatalog();

                var precipPaths = catalog
                    .Where(p => p.FullPath.Contains("PRECIP-INC", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (!precipPaths.Any())
                {
                    //System.Diagnostics.Debug.WriteLine("No PRECIP-INC record found in DSS file.");
                    return results;
                }

                var selected = string.IsNullOrEmpty(preferredRun)
                    ? precipPaths.First()
                    : precipPaths.FirstOrDefault(p =>
                        p.FullPath.Contains(preferredRun, StringComparison.OrdinalIgnoreCase))
                      ?? precipPaths.First();

                System.Diagnostics.Debug.WriteLine($"Reading PRECIP-INC pathname: {selected.FullPath}");

                var timeSeries = dss.GetTimeSeries(new DssPath(selected.FullPath));

                if (timeSeries == null || timeSeries.Count == 0) return results;

                for (int i = 0; i < timeSeries.Count; i++)
                {
                    var value = timeSeries.Values[i];
                    if (double.IsNaN(value) || value <= -900) continue;
                    results.Add(new HyetographRecord(timeSeries.Times[i], value));
                }

                //System.Diagnostics.Debug.WriteLine($"Read {results.Count} PRECIP-INC records.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ReadPrecipInc error: {ex.Message}");
            }

            return results;
        }

        public static List<HyetographRecord> ReadFlow(string dssFilePath, string preferredRun = "")
        {
            var results = new List<HyetographRecord>();

            if (!File.Exists(dssFilePath)) return results;

            try
            {
                using var dss = new DssReader(dssFilePath);
                var catalog = dss.GetCatalog();

                var flowPaths = catalog
                    .Where(p => p.FullPath.Contains("/FLOW/", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var flowPaths_Combine = catalog
                    .Where(p => p.FullPath.Contains("/FLOW-COMBINE/", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var flowPaths_Base = catalog
                    .Where(p => p.FullPath.Contains("/FLOW-BASE/", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var flowPaths_Direct = catalog
                    .Where(p => p.FullPath.Contains("/FLOW-DIRECT/", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var flowPaths_Unit_Graph = catalog
                    .Where(p => p.FullPath.Contains("/FLOW-UNIT-GRAPH/", StringComparison.OrdinalIgnoreCase))
                    .ToList();


                if (!flowPaths.Any())
                {
                    System.Diagnostics.Debug.WriteLine("No FLOW record found in DSS file.");
                    return results;
                }

                var selected = string.IsNullOrEmpty(preferredRun)
                    ? flowPaths.First()
                    : flowPaths.FirstOrDefault(p =>
                        p.FullPath.Contains(preferredRun, StringComparison.OrdinalIgnoreCase))
                      ?? flowPaths.First();

                System.Diagnostics.Debug.WriteLine($"Reading FLOW pathname: {selected.FullPath}");

                var timeSeries = dss.GetTimeSeries(new DssPath(selected.FullPath));

                if (timeSeries == null || timeSeries.Count == 0) return results;

                for (int i = 0; i < timeSeries.Count; i++)
                {
                    var value = timeSeries.Values[i];
                    if (double.IsNaN(value) || value <= -900) continue;
                    results.Add(new HyetographRecord(timeSeries.Times[i], value));
                }

                //System.Diagnostics.Debug.WriteLine($"Read {results.Count} FLOW records.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ReadFlow error: {ex.Message}");
            }

            return results;
        }





        public static List<HyetographRecord> ReadByPartB(string dssFilePath, string partB, string preferredRun = "")
        {
            var results = new List<HyetographRecord>();
            if (!File.Exists(dssFilePath)) return results;

            try
            {
                using var dss = new DssReader(dssFilePath);
                var catalog = dss.GetCatalog();

                var paths = catalog
                    .Where(p => p.FullPath.Contains($"/{partB}/", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (!paths.Any())
                {
                    //System.Diagnostics.Debug.WriteLine($"No {partB} record found in DSS file.");
                    return results;
                }

                var selected = string.IsNullOrEmpty(preferredRun)
                    ? paths.First()
                    : paths.FirstOrDefault(p =>
                        p.FullPath.Contains(preferredRun, StringComparison.OrdinalIgnoreCase))
                      ?? paths.First();

                System.Diagnostics.Debug.WriteLine($"Reading {partB} pathname: {selected.FullPath}");

                var timeSeries = dss.GetTimeSeries(new DssPath(selected.FullPath));

                //System.Diagnostics.Debug.WriteLine($"TimeSeries count: {timeSeries?.Count}");

                if (timeSeries == null || timeSeries.Count == 0) return results;

                for (int i = 0; i < timeSeries.Count; i++)
                {
                    var value = timeSeries.Values[i];
                    if (double.IsNaN(value) || value <= -900) continue;
                    results.Add(new HyetographRecord(timeSeries.Times[i], value));
                }

                //System.Diagnostics.Debug.WriteLine($"Read {results.Count} {partB} records.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ReadByPartB({partB}) error: {ex.Message}");
            }

            return results;
        }


    }
}