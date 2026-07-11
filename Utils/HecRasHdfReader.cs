using HydroExplorer.View;
using PureHDF;
using PureHDF.VOL.Native;
using System.IO;
using System.Runtime.InteropServices;



namespace HydroExplorer.Utils
{
    public class HecRasHdfReader
    {
        private const string BasePath =
       "/Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/";

        private const string BasePathAV =
            "/Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Additional Variables/";


        public static List<WSELTableOxy> ReadWSELTableOxy(string? hdfPathA, string? hdfPathB, string proName, out string? warning)
        {
            warning = null;
            bool hasA = !string.IsNullOrEmpty(hdfPathA) && File.Exists(hdfPathA);
            bool hasB = !string.IsNullOrEmpty(hdfPathB) && File.Exists(hdfPathB);

            if (!hasA || !hasB) return [];

            if (!hasB && hasA) return ReadWSELTableOxySingle(hdfPathA!, proName) ?? [];
            if (!hasA && hasB) return ReadWSELTableOxySingle(hdfPathB!, proName) ?? [];

            using var fileA = OpenHdf(hdfPathA!);
            using var fileB = OpenHdf(hdfPathB!);

            if (!HasSteadyResults(fileA) && !HasSteadyResults(fileB))
            {
                System.Diagnostics.Debug.WriteLine(
                    "ReadWSELTableOxy: both plans have no Steady Output results, skipping.");
                return [];
            }

            var (riverA, reachA, staA) = ReadCrossSectionAttrs(fileA);

            var (riverB, reachB, staB) = ReadCrossSectionAttrs(fileB);

            if (staA.Length == 0 || staB.Length == 0)
            {
                System.Diagnostics.Debug.WriteLine("ReadWSELTableOxy: empty cross-section attrs, skipping.");
                return [];
            }

            var profileA = ReadSteadyProfileNames(fileA);
            var profileB = ReadSteadyProfileNames(fileB);

            if (profileA.Count == 0 || profileB.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("ReadWSELTableOxy: empty profile names, skipping.");
                return [];
            }

            int proNA = profileA.IndexOf(proName);
            int proNB = profileB.IndexOf(proName);

            // Profile mismatch is now a warning, not a stoppage — fall back to each
            // plan's first profile so the grid still populates (matching the
            // fallback ReadWSELTableOxySingle already uses), but flag it clearly
            // since A and B are no longer necessarily the same flow event.
            if (proNA == -1 || proNB == -1)
            {
                warning = $"Plans reference different flow profiles — '{proName}' not found in " +
                          $"{(proNA == -1 ? "Plan A" : "Plan B")}. Showing each plan's first profile instead; " +
                          "comparison may not represent the same event.";
                //System.Diagnostics.Debug.WriteLine(
                //    $"ReadWSELTableOxy: {warning} A has [{string.Join(", ", profileA)}], B has [{string.Join(", ", profileB)}].");

                if (proNA == -1) proNA = 0;
                if (proNB == -1) proNB = 0;
            }

            var qTotalA = fileA.Dataset(BasePathAV + "Flow Total").Read<float[,]>();
            var wsElevA = fileA.Dataset(BasePath + "Water Surface").Read<float[,]>();
            var maxDepths = fileA.Dataset(BasePathAV + "Maximum Depth Total").Read<float[,]>();
            var minChEl = CalcCrossSectionMinElev(wsElevA, maxDepths);

            var qTotalB = fileB.Dataset(BasePathAV + "Flow Total").Read<float[,]>();
            var wsElevB = fileB.Dataset(BasePath + "Water Surface").Read<float[,]>();

            int rowsA = wsElevA.GetLength(0), colsA = wsElevA.GetLength(1);
            int rowsB = wsElevB.GetLength(0), colsB = wsElevB.GetLength(1);

            // Guard: profile index in bounds
            if (proNA >= rowsA || proNB >= rowsB) return [];

            // Guard: column counts must match attr lengths
            if (colsA != staA.Length || colsB != staB.Length)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"ReadWSELTableOxy: column/attr length mismatch — colsA={colsA} staA={staA.Length}, colsB={colsB} staB={staB.Length}");
                return [];
            }

            // Key by River+Reach+Station composite — station number alone is not unique
            // across a multi-reach model; keying only by station caused cross-reach
            // misalignment (e.g. Oak Spr Overflow sta 571 matching TAN 3 sta 571).
            static string CompositeKey(string river, string reach, string sta) =>
                $"{river.Trim()}|{reach.Trim()}|{sta.Trim()}";

            var staBIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int j = 0; j < staB.Length && j < colsB; j++)
            {
                if (!staBIndex.TryAdd(CompositeKey(riverB[j], reachB[j], staB[j]), j))
                    System.Diagnostics.Debug.WriteLine(
                        $"Duplicate key in Plan B: {CompositeKey(riverB[j], reachB[j], staB[j])} at j={j} (first kept)");
            }

            var results = new List<WSELTableOxy>();

            for (int i = 0; i < staA.Length && i < colsA; i++)
            {
                string key = CompositeKey(riverA[i], reachA[i], staA[i]);

                if (!staBIndex.TryGetValue(key, out int j))
                    continue;

                double wselA = Math.Round(wsElevA[proNA, i], 2);
                double wselB = Math.Round(wsElevB[proNB, j], 2);

                results.Add(new WSELTableOxy
                {
                    River = riverA[i],
                    Reach = reachA[i],
                    RiverSta = staA[i].Trim(),
                    Profile = profileA[proNA],
                    QTotalA = qTotalA[proNA, i],
                    MinChEl = minChEl[proNA, i],
                    WSElevA = wselA,
                    QTotalB = qTotalB[proNB, j],
                    WSElevB = wselB,
                    DELTA = Math.Round(wselB - wselA, 2)
                });
            }

            return results
                .OrderBy(r => r.Reach, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(r => ParseStation(r.RiverSta))
                .ToList();
        }

        /// <summary>
        /// Parses a RiverSta string to a sortable numeric value, tolerating the
        /// same formatting HEC-RAS emits (thousands separators, "*" interpolated-
        /// section markers). Non-numeric stations sort last.
        /// </summary>
        private static double ParseStation(string? riverSta)
        {
            if (string.IsNullOrEmpty(riverSta)) return double.MinValue;
            string cleaned = riverSta.Replace(",", "").Replace("*", "").Trim();
            return double.TryParse(cleaned, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double sta)
                ? sta : double.MinValue;
        }

        public static List<WSELTableOxy>? ReadWSELTableOxySingle(string planPath, string proName)
        {
            if (string.IsNullOrEmpty(planPath) || !File.Exists(planPath)) return [];

            var (has1D, has2D) = GetModelDimensions(planPath);
            if (has2D && !has1D)
            {
                System.Diagnostics.Debug.WriteLine("ReadWSELTableOxySingle: 2D model detected, skipping.");
                return [];
            }

            using var fileA = OpenHdf(planPath);

            if (!HasSteadyResults(fileA))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"ReadWSELTableOxySingle: '{planPath}' has no Steady Output results (unsteady plan or not yet computed), skipping.");
                return null;
            }

            var (rivers, reaches, stations) = ReadCrossSectionAttrs(fileA);
            var profileA = ReadSteadyProfileNames(fileA);

            int proNA = profileA.IndexOf(proName);
            if (proNA == -1)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"ReadWSELTableOxySingle: '{proName}' not found in profiles [{string.Join(", ", profileA)}], defaulting to 0.");
                proNA = 0;
            }

            var qTotalA = fileA.Dataset(BasePathAV + "Flow Total").Read<float[,]>();
            var wsElevA = fileA.Dataset(BasePath + "Water Surface").Read<float[,]>();
            var maxDepths = fileA.Dataset(BasePathAV + "Maximum Depth Total").Read<float[,]>();
            var minChEl = CalcCrossSectionMinElev(wsElevA, maxDepths);

            int rowsA = wsElevA.GetLength(0);
            int colsA = wsElevA.GetLength(1);

            if (proNA >= rowsA)
            {
                System.Diagnostics.Debug.WriteLine($"ReadWSELTableOxySingle: proNA={proNA} >= rowsA={rowsA}.");
                return null;
            }

            int cols = Math.Min(colsA, stations.Length);
            var results = new List<WSELTableOxy>(cols);

            for (int i = 0; i < cols; i++)
            {
                results.Add(new WSELTableOxy
                {
                    River = rivers[i],
                    Reach = reaches[i],
                    RiverSta = stations[i],
                    Profile = profileA[proNA],
                    QTotalA = qTotalA[proNA, i],
                    MinChEl = minChEl[proNA, i],
                    WSElevA = Math.Round(wsElevA[proNA, i], 2),
                    QTotalB = float.NaN,
                    WSElevB = double.NaN,
                    DELTA = double.NaN
                });
            }

            return results
                .OrderBy(r => r.Reach, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(r => ParseStation(r.RiverSta))
                .ToList();
        }







        public struct CrossSectionAttr
        {
            [H5Name("River")]
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
            public string River;

            [H5Name("Reach")]
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
            public string Reach;

            [H5Name("Station")]
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 8)]
            public string Station;

            [H5Name("Name")]
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
            public string Name;
        }

        public struct CrossSectionAVMaxDepth
        {
            [H5Name("Variable_Unit")]
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string Variable_Unit;
        }




        private static (string[] Rivers, string[] Reaches, string[] Stations) ReadCrossSectionAttrs(NativeFile file)
        {
            var raw = file.Dataset("/Results/Steady/Output/Geometry Info/Cross Section Attributes")
                          .Read<CrossSectionAttr[]>();

            var rivers = new string[raw.Length];
            var reaches = new string[raw.Length];
            var stations = new string[raw.Length];

            for (int i = 0; i < raw.Length; i++)
            {
                rivers[i] = raw[i].River.Trim();
                reaches[i] = raw[i].Reach.Trim();
                stations[i] = raw[i].Station.Trim();
            }

            return (rivers, reaches, stations);
        }

        private static string[] ReadCrossSectionAttrName(NativeFile file)
        {
            var raw = file.Dataset("/Results/Steady/Output/Geometry Info/Cross Section Attributes").Read<CrossSectionAttr[]>();
            int rows = raw.Length;

            var strings = new string[rows];

            for (int i = 0; i < rows; i++)
            {
                strings[i] = raw[i].Name
                    .Trim();
            }
            return strings;
        }


        private static List<string> ReadSteadyProfileNames(NativeFile file)
        {
            var raw = file.Dataset("/Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Profile Names").Read<string[]>();

            var strings = new List<string>(raw.Length);
            for (int i = 0; i < raw.Length; i++)
                strings.Add(raw[i]);

            return strings;
        }


        private static string[] ReadPlanNames(NativeFile file)
        {
            var raw = file.Dataset("/Plan Data/Plan Name").Read<string[]>();
            int rows = raw.Length;

            var strings = new string[rows];

            for (int i = 0; i < rows; i++)
            {
                strings[i] = raw[i]
                    .Trim();
            }
            return strings;
        }

        public static List<string> GetProfileNames(string filePath)
        {
            using var file = OpenHdf(filePath);

            if (!HasSteadyResults(file))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"GetProfileNames: '{filePath}' has no Steady Output results, returning empty.");
                return [];
            }

            return ReadSteadyProfileNames(file);
        }

        public static string GetPlanName(string filePath)
        {
            using var file = OpenHdf(filePath);

            try
            {
                return file.Group("/Plan Data/Plan Information").Attribute("Plan Name").Read<string>().Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        public static void InspectPlanData(string filePath)
        {
            using var file = OpenHdf(filePath);
            var group = file.Group("/Plan Data/Plan Information");
            foreach (var link in group.Children())
            {
                System.Diagnostics.Debug.WriteLine($"Plan Information child: {link.Name}");
            }
            foreach (var attr in group.Attributes())
            {
                System.Diagnostics.Debug.WriteLine($"Plan Information attribute: {attr.Name}");
            }
        }

        private static float[] ReadCrossSectionStaElev(NativeFile file)
        {
            var raw = file.Dataset("/Geometry/Cross Sections/Station Elevation Values").Read<float[,]>();
            System.Diagnostics.Debug.WriteLine($"staElev........................: {raw[0, 0]}");

            var minValue = raw[0, 1];
            int rows = raw.GetLength(0);

            var minValues = new float[rows];
            for (int i = 0; i < rows; i++)
            {
                if (raw[i, 1] < minValue)
                {
                    minValue = raw[i, 1];
                    minValues[i] = minValue;
                }
            }
            return minValues;
        }

        private static float[,] ReadCrossSectionMaxDepth(NativeFile file)
        {
            var raw = file.Dataset("/Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Additional Variables/Maximum Depth Total").Read<float[,]>();
            System.Diagnostics.Debug.WriteLine($"maxDepth........................: {raw[0, 0]}");

            var maxValue = raw[0, 1];
            int rows = raw.GetLength(0);
            int cols = raw.GetLength(1);

            var maxValues = new float[rows, cols];

            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    if (raw[i, j] > maxValue)
                    {
                        maxValue = raw[i, j];
                        maxValues[i, j] = maxValue;
                    }
                }
            }
            return maxValues;
        }

        private static float[,] CalcCrossSectionMinElev(float[,] wsEls, float[,] maxDepths)
        {
            int rows = wsEls.GetLength(0);
            int cols = wsEls.GetLength(1);
            var minValues = new float[rows, cols];

            for (int j = 0; j < cols; j++)
            {
                float stationMin = float.MaxValue;
                for (int i = 0; i < rows; i++)
                {
                    float minCh = wsEls[i, j] - maxDepths[i, j];
                    if (minCh < stationMin) stationMin = minCh;
                }

                float rounded = Convert.ToSingle(Math.Round(stationMin, 2));
                for (int i = 0; i < rows; i++)
                    minValues[i, j] = rounded;
            }

            return minValues;
        }


        private static float[,] CalcFroude(NativeFile file, float[,] velCh)
        {
            //fr = v / math.sqrt(g * d_hyd)
            var hydDepths = file.Dataset(BasePathAV + "Hydraulic Depth Channel").Read<float[,]>();

            int rows = hydDepths.GetLength(0);
            int cols = hydDepths.GetLength(1);

            float[] g = new float[rows];
            Array.Fill(g, 32.2f);

            var frVals = new float[rows, cols];
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    frVals[i, j] = Convert.ToSingle(Math.Round(velCh[i, j] / Math.Sqrt(g[i] * hydDepths[i, j]), 2));
                }
            }
            return frVals;
        }

        internal static NativeFile OpenHdf(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("HDF path cannot be null or empty.", nameof(path));

            byte[] bytes = File.ReadAllBytes(path);
            return H5File.Open(new MemoryStream(bytes));
        }

        public static bool Is2DModel(string hdfPath)
        {
            using var file = OpenHdf(hdfPath);
            try
            {
                var group = file.Group("/Geometry/2D Flow Areas");
                return group.Children().Any();
            }
            catch
            {
                return false;
            }
        }

        public static (bool has1D, bool has2D) GetModelDimensions(string hdfPath)
        {
            using var file = OpenHdf(hdfPath);

            bool has1D = false;
            bool has2D = false;

            try { has1D = file.Group("/Geometry/Cross Sections").Children().Any(); }
            catch { }

            try { has2D = file.Group("/Geometry/2D Flow Areas").Children().Any(); }
            catch { }

            return (has1D, has2D);
        }

        /// <summary>
        /// Checks whether this plan has a Steady Output results tree at all —
        /// an unsteady-flow plan, or a steady plan that hasn't been computed yet,
        /// will have valid 1D geometry but no /Results/Steady/... group, which
        /// would otherwise throw an unhandled exception deep inside PureHDF
        /// when ReadCrossSectionAttrs/ReadSteadyProfileNames try to read from it.
        /// </summary>
        private static bool HasSteadyResults(NativeFile file)
        {
            try
            {
                _ = file.Dataset("/Results/Steady/Output/Geometry Info/Cross Section Attributes");
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}