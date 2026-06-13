using HydroExplorer.View;
using PureHDF;
using PureHDF.VOL.Native;
using System.Runtime.InteropServices;
using System.IO;



namespace HydroExplorer.Utils
{
    public class HecRasHdfReader
    {
        private const string BasePath =
       "/Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/";

        private const string BasePathAV =
            "/Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Additional Variables/";
        

        public static List<WSELTable> ReadWSELTable(string planPathA, string planPathB, string proName)
        {
            using var fileA = H5File.OpenRead(planPathA);

            var river = ReadCrossSectionAttrRiver(fileA);
            var reach = ReadCrossSectionAttrReach(fileA);
            var riverSta = ReadCrossSectionAttrStation(fileA);
            var profile = ReadSteadyProfileNames(fileA);


            var profileArr = new string[river.Length];

            int proN = profile.IndexOf(proName);
            if (proN == -1) proN = 0;

            Array.Fill(profileArr, profile[proN]);

            var qTotalA = fileA.Dataset(BasePathAV + "Flow Total").Read<float[,]>();
            var wsElevA = fileA.Dataset(BasePath + "Water Surface").Read<float[,]>();

            using var fileB = H5File.OpenRead(planPathB);
            var qTotalB = fileB.Dataset(BasePathAV + "Flow Total").Read<float[,]>();
            var wsElevB = fileB.Dataset(BasePath + "Water Surface").Read<float[,]>();


            var results = new List<WSELTable>(riverSta.Length);

            for (int i = 0; i < riverSta.Length; i++)
            {
                results.Add(new WSELTable
                {
                    River = river[i],
                    Reach = reach[i],
                    RiverSta = riverSta[i],
                    Profile = profileArr[proN],
                    QTotalA = qTotalA[proN, i],
                    WSElevA = Math.Round(wsElevA[proN, i], 2),
                    QTotalB = qTotalB[proN, i],
                    WSElevB = Math.Round(wsElevB[proN, i], 2),
                    DELTA = Math.Round(wsElevB[proN, i] - (wsElevA[proN, i]), 2)
                });
            }
            return results;
        }


        public static List<WSELTableOxy> ReadWSELTableOxy(string? hdfPathA, string? hdfPathB, string proName)
        {
            bool hasA = !string.IsNullOrEmpty(hdfPathA) && File.Exists(hdfPathA);
            bool hasB = !string.IsNullOrEmpty(hdfPathB) && File.Exists(hdfPathB);

            if (!hasA && !hasB) return [];
            if (!hasB && hasA) return ReadWSELTableOxySingle(hdfPathA!, proName) ?? [];
            if (!hasA && hasB) return ReadWSELTableOxySingle(hdfPathB!, proName) ?? [];

            using var fileA = H5File.OpenRead(hdfPathA!);
            using var fileB = H5File.OpenRead(hdfPathB!);

            // Read geometry from each plan independently
            var riverA = ReadCrossSectionAttrRiver(fileA);
            var reachA = ReadCrossSectionAttrReach(fileA);
            var staA = ReadCrossSectionAttrStation(fileA);

            var staB = ReadCrossSectionAttrStation(fileB);

            var profileA = ReadSteadyProfileNames(fileA);
            var profileB = ReadSteadyProfileNames(fileB);

            int proNA = profileA.IndexOf(proName);
            if (proNA == -1) proNA = 0;

            int proNB = profileB.IndexOf(proName);
            if (proNB == -1)
            {
                //System.Diagnostics.Debug.WriteLine(
                //    $"ReadWSELTableOxy: '{proName}' not in fileB [{string.Join(", ", profileB)}], using 0.");
                proNB = 0;
            }

            var qTotalA = fileA.Dataset(BasePathAV + "Flow Total").Read<float[,]>();
            var wsElevA = fileA.Dataset(BasePath + "Water Surface").Read<float[,]>();
            var maxDepths = fileA.Dataset(BasePathAV + "Maximum Depth Total").Read<float[,]>();
            var minChEl = CalcCrossSectionMinElev(wsElevA, maxDepths);

            var qTotalB = fileB.Dataset(BasePathAV + "Flow Total").Read<float[,]>();
            var wsElevB = fileB.Dataset(BasePath + "Water Surface").Read<float[,]>();

            int rowsA = wsElevA.GetLength(0), colsA = wsElevA.GetLength(1);
            int rowsB = wsElevB.GetLength(0), colsB = wsElevB.GetLength(1);

            if (proNA >= rowsA || proNB >= rowsB) return [];

            // Build a lookup from station string → index in Plan B
            var staBIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int j = 0; j < staB.Length && j < colsB; j++)
                staBIndex.TryAdd(staB[j].Trim(), j);

            var results = new List<WSELTableOxy>();

            for (int i = 0; i < staA.Length && i < colsA; i++)
            {
                string sta = staA[i].Trim();

                // Find matching cross section in Plan B by station label
                if (!staBIndex.TryGetValue(sta, out int j))
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"ReadWSELTableOxy: station '{sta}' not found in Plan B — skipping.");
                    continue;
                }

                double wselA = Math.Round(wsElevA[proNA, i], 2);
                double wselB = Math.Round(wsElevB[proNB, j], 2);

                results.Add(new WSELTableOxy
                {
                    River = riverA[i],
                    Reach = reachA[i],
                    RiverSta = sta,
                    Profile = profileA[proNA],
                    QTotalA = qTotalA[proNA, i],
                    MinChEl = minChEl[proNA, i],
                    WSElevA = wselA,
                    QTotalB = qTotalB[proNB, j],
                    WSElevB = wselB,
                    DELTA = Math.Round(wselB - wselA, 2)
                });
            }

            return results;
        }



        public static List<WSELTableOxy>? ReadWSELTableOxySingle(string planPathA, string proName)
        {
            if (string.IsNullOrEmpty(planPathA) || !File.Exists(planPathA))
            {
                System.Diagnostics.Debug.WriteLine($"ReadWSELTableOxySingle: HDF path missing: '{planPathA}'.");
                return null;
            }

            using var fileA = H5File.OpenRead(planPathA);

            var river = ReadCrossSectionAttrRiver(fileA);
            var reach = ReadCrossSectionAttrReach(fileA);
            var riverSta = ReadCrossSectionAttrStation(fileA);
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

            int cols = Math.Min(colsA, riverSta.Length);
            var results = new List<WSELTableOxy>(cols);

            for (int i = 0; i < cols; i++)
            {
                results.Add(new WSELTableOxy
                {
                    River = river[i],
                    Reach = reach[i],
                    RiverSta = riverSta[i],
                    Profile = profileA[proNA],
                    QTotalA = qTotalA[proNA, i],
                    MinChEl = minChEl[proNA, i],
                    WSElevA = Math.Round(wsElevA[proNA, i], 2),
                    QTotalB = float.NaN,   // no second source
                    WSElevB = double.NaN,  // no second source
                    DELTA = double.NaN   // can't compute
                });
            }

            return results;
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





        private static string[] ReadCrossSectionAttrRiver(NativeFile file)
        {
            var raw = file.Dataset("/Results/Steady/Output/Geometry Info/Cross Section Attributes").Read<CrossSectionAttr[]>();
            int rows = raw.Length;

            var strings = new string[rows];

            for (int i = 0; i < rows; i++)
            {
                strings[i] = raw[i].River
                    .Trim();
            }
            return strings;
        }

        private static string[] ReadCrossSectionAttrReach(NativeFile file)
        {
            var raw = file.Dataset("/Results/Steady/Output/Geometry Info/Cross Section Attributes").Read<CrossSectionAttr[]>();
            int rows = raw.Length;

            var strings = new string[rows];

            for (int i = 0; i < rows; i++)
            {
                strings[i] = raw[i].Reach
                    .Trim();
            }
            return strings;
        }

        private static string[] ReadCrossSectionAttrStation(NativeFile file)
        {
            var raw = file.Dataset("/Results/Steady/Output/Geometry Info/Cross Section Attributes").Read<CrossSectionAttr[]>();
            int rows = raw.Length;

            var strings = new string[rows];

            for (int i = 0; i < rows; i++)
            {
                strings[i] = raw[i].Station
                    .Trim();
            }
            return strings;
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
            //var raw = file.Dataset("/Results/Steady/Output/Geometry Info/Cross Section Attributes").Read<CrossSectionAttr[]>();
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
            using var file = H5File.OpenRead(filePath);
            return ReadSteadyProfileNames(file);
        }

        public static string GetPlanName(string filePath)
        {
            using var file = H5File.OpenRead(filePath);

            try
            {
                return file.Group("/Plan Data/Plan Information").Attribute("Plan Name").Read<string>().Trim();
            }
            catch
                {
                    //return Path.GetFileNameWithoutExtension(filePath);
                    return string.Empty;
                }
        }

        public static void InspectPlanData(string filePath)
        {
            using var file = H5File.OpenRead(filePath);
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

            // Initialize min value with the first element of the array
            var minValue = raw[0, 1];
            int rows = raw.GetLength(0);

            var minValues = new float[rows];
            // Loop through the array starting from the second element
            for (int i = 0; i < rows; i++)
            {
                if (raw[i, 1] < minValue)
                {
                    // Update minValue if the current element is smaller
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

            // Initialize min value with the first element of the array
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
            int proFileID = 1;

            var minValue = wsEls[0, proFileID] - maxDepths[0, proFileID];

            int rows = wsEls.GetLength(0);
            int cols = wsEls.GetLength(1);

            var minValues = new float[rows, cols];
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    float minCh = wsEls[i, j] - maxDepths[i, j];
                    if (minCh < minValue)
                    {
                        minValues[i, j] = Convert.ToSingle(Math.Round(minCh, 2));
                    }
                    else
                    {
                        minValues[i, j] = Convert.ToSingle(Math.Round(minValue, 2));
                    }
                }
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
    }
}