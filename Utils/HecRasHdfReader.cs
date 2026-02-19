using HydroExplorer.MVVM.View;
using PureHDF;
using PureHDF.VOL.Native;
using System.Runtime.InteropServices;


namespace HydroExplorer.Utils
{
    public class HecRasHdfReader
    {
        private const string BasePath =
       "/Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/";

        private const string BasePathAV = 
            "/Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Additional Variables/";


        public List<HecRasProfileWselResult> ReadProfileWsel(string filePath)
        {
            using var file = H5File.OpenRead(filePath);

            var riverSta = ReadCrossSectionAttrStation(file);
            var wsElev = file.Dataset(BasePath + "Water Surface").Read<float[,]>();
            
            //System.Diagnostics.Debug.WriteLine($"minChEl........................: {minChEl[0,0]}");

            int proN = 5;

            int resN = wsElev.GetLength(1);
            var results = new List<HecRasProfileWselResult>(resN);

            for (int i = 0; i < resN; i++)
            {
                results.Add(new HecRasProfileWselResult
                {
                    WSElev = wsElev[proN, i],
                    RiverSta = riverSta[i],
                });
            }
            return results;
        }


        public List<HecRasProfileResult> ReadProfileSummary(string filePath)
        {
            using var file = H5File.OpenRead(filePath);

            int proN = 5;

            var river = ReadCrossSectionAttrRiver(file);
            var reach = ReadCrossSectionAttrReach(file);
            var riverSta = ReadCrossSectionAttrStation(file);
            var profile = ReadSteadyProfileNames(file);

            var profileArr = new string[river.Length];
            Array.Fill(profileArr, profile[proN]);

            var qTotal = file.Dataset(BasePathAV + "Flow Total").Read<float[,]>();
            var wsElev = file.Dataset(BasePath + "Water Surface").Read<float[,]>();
            var maxDepths = file.Dataset(BasePathAV + "Maximum Depth Total").Read<float[,]>();

            var minChEl = CalcCrossSectionMinElev("5", wsElev, maxDepths);

            var critWs = file.Dataset(BasePathAV + "Critical Water Surface").Read<float[,]>();
            var egElev = file.Dataset(BasePath + "Energy Grade").Read<float[,]>();
            var egSlope = file.Dataset(BasePathAV + "EG Slope").Read<float[,]>();
            var velChnl = file.Dataset(BasePathAV + "Velocity Total").Read<float[,]>();
            var flowArea = file.Dataset(BasePathAV + "Area Flow Total").Read<float[,]>();
            var topWidth = file.Dataset(BasePathAV + "Top Width Total").Read<float[,]>();
            var froude = CalcFroude(file, "5", velChnl);

            var results = new List<HecRasProfileResult>(riverSta.Length);

            for (int i = 0; i < riverSta.Length; i++)
            {
                results.Add(new HecRasProfileResult
                {
                    River = river[i],
                    Reach = reach[i],
                    RiverSta = riverSta[i],
                    Profile = profileArr[proN],
                    QTotal = qTotal[proN, i],
                    MinChEl = minChEl[proN, i],
                    WSElev = Math.Round(wsElev[proN, i], 2),
                    MaxDepths = Math.Round(maxDepths[proN, i], 2),
                    CritWS = Math.Round(critWs[proN, i], 2),
                    EGElev = Math.Round(egElev[proN, i], 2),
                    EGSlope = Math.Round(egSlope[proN, i], 4),
                    VelChnl = Math.Round(velChnl[proN, i], 1),
                    FlowArea = Math.Round(flowArea[proN, i]),
                    TopWidth = Math.Round(topWidth[proN, i]),
                    FrChnl = Math.Round(froude[proN, i], 2)
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


        private static string[] ReadSteadyProfileNames(NativeFile file)
        {
            //var raw = file.Dataset("/Results/Steady/Output/Geometry Info/Cross Section Attributes").Read<CrossSectionAttr[]>();
            var raw = file.Dataset("/Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Profile Names").Read<string[]>();
            int rows = raw.Length;

            var strings = new string[rows];

            for (int i = 0; i < rows; i++)
            {
                strings[i] = raw[i]
                    .Trim();
            }
            return strings;
        }


        private static float[] ReadCrossSectionStaElev(NativeFile file)
        {
            var raw = file.Dataset("/Geometry/Cross Sections/Station Elevation Values").Read<float[,]>();
            System.Diagnostics.Debug.WriteLine($"staElev........................: {raw[0, 0]}");

            // Initialize min value with the first element of the array
            var minValue = raw[0,1];
            int rows = raw.GetLength(0);

            var minValues = new float[rows];
            // Loop through the array starting from the second element
            for (int i = 0; i < rows; i++)
            {
                if (raw[i,1] < minValue)
                {
                    // Update minValue if the current element is smaller
                    minValue = raw[i,1];
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


        private static float[,] CalcCrossSectionMinElev(string proFile, float[,] wsEls, float[,] maxDepths)
        {
            int proFileID = 1;
            
            var minValue = wsEls[0, proFileID] - maxDepths[0, proFileID];

            int rows = wsEls.GetLength(0);
            int cols = wsEls.GetLength(1);

            var minValues = new float[rows, cols];
            float minCh = 10000;

            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    minCh = wsEls[i, j] - maxDepths[i, j];
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


        private static float[,] CalcFroude(NativeFile file, string proFile, float[,] velCh)
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


        private static string[] ReadCrossSectionAttrRiver(NativeFile file, string path, string attrString)
        {
            CrossSectionAttr attr = new CrossSectionAttr();
            attr.River = attrString;
            
            var group = file.Group("Results/Steady/Output/Geometry Info");
            System.Diagnostics.Debug.WriteLine($"group........................: {group}");

            foreach (var link in group.Children())
            {
                var message = link switch
                {
                    IH5Group childGroup => $"I am a group and my name is '{childGroup.Name}'.",
                    IH5Dataset childDataset => $"I am a dataset, call me '{childDataset.Name}'.",
                    IH5CommitedDatatype childDatatype => $"I am the data type '{childDatatype.Name}'.",
                    IH5UnresolvedLink lostLink => $"I cannot find my link target =( shame on '{lostLink.Name}'.",
                    _ => throw new Exception("Unknown link type")
                };

                System.Diagnostics.Debug.WriteLine(message);
            }

            var dataset = group.Dataset("/Results/Steady/Output/Geometry Info/Cross Section Attributes");
            System.Diagnostics.Debug.WriteLine($"dataset........................: {dataset}");

            var raw = file.Dataset("/Results/Steady/Output/Geometry Info/Cross Section Attributes").Read<CrossSectionAttr[]>();
            System.Diagnostics.Debug.WriteLine($"raw........................: {raw}");
            System.Diagnostics.Debug.WriteLine($"raw.Length........................: {raw.Length}");

            System.Diagnostics.Debug.WriteLine($"raw[].Reach........................: {raw[2].River}");

            int rows = raw.Length;
            var strings = new string[rows];

            for (int i = 0; i < rows; i++)
            {
                strings[i] = raw[i].River
                    .Trim();
            }
            foreach (var r in strings)
            {
                //System.Diagnostics.Debug.WriteLine(string.Join(", ", row));
                System.Diagnostics.Debug.WriteLine(r);
            }
            return strings;
        }


        private static string[] ReadStringDataset(NativeFile file, string path)
        {
            //var raw = file.Dataset(path).Read<byte[,]>();
            var group = file.Group("Results/Steady/Output/Geometry Info");
            System.Diagnostics.Debug.WriteLine($"group........................: {group}");
            
            //var commitedDataType = file.Group("/Results/Steady/Output/Geometry Info/Cross Section Attributes");
            //var unknownObject = file.Get("/Results/Steady/Output/Geometry Info/Cross Section Attributes");

            foreach (var link in group.Children())
            {
                var message = link switch
                {
                    IH5Group childGroup => $"I am a group and my name is '{childGroup.Name}'.",
                    IH5Dataset childDataset => $"I am a dataset, call me '{childDataset.Name}'.",
                    IH5CommitedDatatype childDatatype => $"I am the data type '{childDatatype.Name}'.",
                    IH5UnresolvedLink lostLink => $"I cannot find my link target =( shame on '{lostLink.Name}'.",
                    _ => throw new Exception("Unknown link type")
                };
                System.Diagnostics.Debug.WriteLine(message);
            }

            var dataset = group.Dataset("/Results/Steady/Output/Geometry Info/Cross Section Attributes");
            //var dataset = group.Dataset("/Results/Steady/Output/Geometry Info/Node Info");
            System.Diagnostics.Debug.WriteLine($"dataset........................: {dataset}");

            var cmem = dataset.GetHashCode;
            System.Diagnostics.Debug.WriteLine($"GetHashCode........................: {cmem}");

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
    }
}