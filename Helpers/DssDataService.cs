using Hec.Dss;
using System.Collections.ObjectModel;
using System.IO;


namespace HydroExplorer.Utils
{
    public class DssDataService
    {
        public static ObservableCollection<DssRecord> Load(string dssFilePath)
        {
            var records = new ObservableCollection<DssRecord>();

            if (!File.Exists(dssFilePath)) return records;

            string? tempPath = null;
            DssGate.Enter();
            try
            {
                tempPath = DssSnapshot.Create(dssFilePath);

                using var dss = new DssReader(tempPath);
                var catalog = dss.GetCatalog();

                foreach (var entry in catalog)
                {
                    // FullPath = "/A/B/C/D/E/F/"
                    var parts = entry.FullPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 6) continue;

                    string units = "", type = "";
                    int count = 0;

                    try
                    {
                        var ts = dss.GetTimeSeries(new DssPath(entry.FullPath));
                        if (ts != null)
                        {
                            units = ts.Units ?? "";
                            type = ts.DataType ?? "";
                            count = ts.Count;
                        }
                    }
                    catch
                    {
                        // Paired data, grids, text records — skip value load
                        type = "non-timeseries";
                    }

                    records.Add(new DssRecord
                    {
                        PartA = parts[0],
                        PartB = parts[1],
                        PartC = parts[2],
                        PartD = parts[3],
                        PartE = parts[4],
                        PartF = parts[5],
                        Units = units,
                        Type = type,
                        Count = count
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DssDataService.Load error: {ex.Message}");
            }
            finally
            {
                DssSnapshot.Cleanup(tempPath);
                DssGate.Exit();
            }

            return records;
        }


        public static TimeSeries? LoadValues(DssRecord record, string dssFilePath)
        {
            if (!File.Exists(dssFilePath)) return null;

            string? tempPath = null;
            DssGate.Enter();
            try
            {
                tempPath = DssSnapshot.Create(dssFilePath);
                using var dss = new DssReader(tempPath);
                return dss.GetTimeSeries(new DssPath(record.Pathname));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadValues error: {ex.Message}");
                return null;
            }
            finally
            {
                DssSnapshot.Cleanup(tempPath);
                DssGate.Exit();
            }
        }
    }
}