using System.Globalization;
using System.IO;

namespace HydroExplorer.Utils
{
    /// <summary>
    /// Reads total subbasin drainage area from a HEC-HMS project (.hms) and its
    /// first referenced basin model (.basin) file. "Total subbasin area" here
    /// means the sum of every Subbasin: block's Area: value in that one basin
    /// file — not a single physical drainage boundary, just the aggregate of
    /// all HMS subbasins defined in the first Basin: entry.
    ///
    /// Assumes "Unit System: English" (area already expressed in square miles
    /// in the .basin file), which is the HEC-HMS convention this project uses.
    /// SI-unit basin files (area in sq km) are not currently handled.
    /// </summary>
    public static class HecHmsBasinReader
    {
        /// <summary>
        /// Resolves the first Basin: block's referenced .basin file from the .hms
        /// project file, then sums all Subbasin: Area: values in it. Returns null
        /// if the .hms file doesn't exist, has no Basin: block with a Filename,
        /// or the referenced .basin file can't be found.
        /// </summary>
        public static double? GetTotalSubbasinAreaSqMi(string? hmsPath)
        {
            if (string.IsNullOrEmpty(hmsPath) || !File.Exists(hmsPath))
                return null;

            string? basinFileName = FindFirstBasinFilename(hmsPath);
            if (string.IsNullOrEmpty(basinFileName))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"HecHmsBasinReader: no Basin: block with a Filename found in '{hmsPath}'.");
                return null;
            }

            string hmsDir = Path.GetDirectoryName(hmsPath) ?? string.Empty;
            string basinPath = Path.Combine(hmsDir, basinFileName);

            if (!File.Exists(basinPath))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"HecHmsBasinReader: basin file not found at '{basinPath}'.");
                return null;
            }

            return SumSubbasinAreas(basinPath);
        }

        /// <summary>
        /// Parses "Basin: &lt;name&gt; ... Filename: &lt;file&gt; ... End:" blocks in
        /// the .hms project file and returns the Filename of the FIRST such block.
        /// </summary>
        private static string? FindFirstBasinFilename(string hmsPath)
        {
            bool inBasinBlock = false;

            foreach (var raw in File.ReadLines(hmsPath))
            {
                string line = raw.Trim();

                if (line.StartsWith("Basin:", StringComparison.OrdinalIgnoreCase))
                {
                    inBasinBlock = true;
                    continue;
                }

                if (inBasinBlock)
                {
                    if (line.StartsWith("Filename:", StringComparison.OrdinalIgnoreCase))
                        return line["Filename:".Length..].Trim();

                    if (line.Equals("End:", StringComparison.OrdinalIgnoreCase))
                        break; // malformed Basin: block with no Filename — stop, nothing to resolve
                }
            }

            return null;
        }

        /// <summary>
        /// Sums every "Subbasin: ... Area: &lt;value&gt; ... End:" block's Area value
        /// found in the given .basin file.
        /// </summary>
        private static double SumSubbasinAreas(string basinPath)
        {
            double total = 0;
            bool inSubbasinBlock = false;

            foreach (var raw in File.ReadLines(basinPath))
            {
                string line = raw.Trim();

                if (line.StartsWith("Subbasin:", StringComparison.OrdinalIgnoreCase))
                {
                    inSubbasinBlock = true;
                    continue;
                }

                if (inSubbasinBlock)
                {
                    if (line.StartsWith("Area:", StringComparison.OrdinalIgnoreCase))
                    {
                        string valueStr = line["Area:".Length..].Trim();
                        if (double.TryParse(valueStr, NumberStyles.Any,
                                CultureInfo.InvariantCulture, out double area))
                            total += area;
                    }
                    else if (line.Equals("End:", StringComparison.OrdinalIgnoreCase))
                    {
                        inSubbasinBlock = false;
                    }
                }
            }

            return total;
        }
    }
}