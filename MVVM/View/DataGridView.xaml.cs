using HydroExplorer.Utils;
using Microsoft.VisualBasic.FileIO;
using PureHDF;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Controls;



namespace HydroExplorer.MVVM.View
{
    public partial class DataGridView : UserControl
    {
        public string filePath = "C:/Temp/TAN_Main_SG.p01.hdf";

        private readonly HecRasHdfReader _reader = new();

        private List<HecRasProfileWselResult> itemsSource1 = [];
        private List<HecRasProfileWselResult> itemsSource2 = [];
        private List<HecRasProfileWselResult> itemsSourceComp = [];

        public DataGridView()
        {
            InitializeComponent();

            //TODO BIND HDF PATH TO APPCONFIG
            dgSimple.ItemsSource = _reader.ReadProfileSummary("C:/Temp/TAN_Main_SG.p01.hdf");
            dgWselProfile2.ItemsSource = _reader.ReadProfileSummary("C:/Temp/TAN_Main_SG.p05.hdf");

            //dgCompare.ItemsSource = _reader.ReadProfileSummary("C:/Temp/TAN_Main_SG.p01.hdf");
            List<HecRasProfileWselResult> calcComp = CalcCompare();
            dgCompare.ItemsSource = calcComp;
            
            var staInfo = getStaInfo(calcComp, "19673");


            //dgCompare.ItemsSource = _reader.ReadProfileWsel("C:/Temp/TAN_Main_SG.p02.hdf");
            System.Diagnostics.Debug.WriteLine($"dgCompare.ItemsSource : {dgCompare.ItemsSource}");
            System.Diagnostics.Debug.WriteLine($"dgCompare.ItemsSource : {dgCompare.ItemsSource}");
        }


        public HecRasProfileWselResult getStaInfo(List<HecRasProfileWselResult> calcComp, string riverSta)
        {
            HecRasProfileWselResult result = calcComp.ElementAt(11);
            var tst = result.WSElev;
            
            System.Diagnostics.Debug.WriteLine($"\n result.WSElev: {tst}");
            System.Diagnostics.Debug.WriteLine($"result.RiverSta: {result.RiverSta} \n");

            return result;
        }


        public List<HecRasProfileWselResult> CalcCompare()
        {
            itemsSource1 = _reader.ReadProfileWsel("C:/Temp/TAN_Main_SG.p01.hdf");
            itemsSource2 = _reader.ReadProfileWsel("C:/Temp/TAN_Main_SG.p05.hdf");

            int resN1 = itemsSource1.Count;
            int resN2 = itemsSource2.Count;

            int idx1 = 0;
            int idx2 = 0;
            string rsta;
            double wselsub = 0;
            List<HecRasProfileWselResult> src;
            List<HecRasProfileWselResult> srcidx;

            int resN = Math.Min(resN1, resN2);
            var results = new List<HecRasProfileWselResult>(resN);

            string[] arr1 = new string[resN];
            string[] arr2 = new string[resN];

            if (resN1 != resN2)
            {
                string[] lstSta = Array.Empty<string>();

                for (int i = 0; i < resN; i++)
                {
                    arr1[i] = itemsSource1[i].RiverSta;
                    arr2[i] = itemsSource2[i].RiverSta;
                }


                for (int i = 0; i < resN; i++)
                {
                    idx1 = i;
                    idx2 = i;
                    rsta = arr1[i];
                    
                    if (resN1 > resN2)
                        rsta = arr2[i];
                    
                    if (itemsSource1[i].RiverSta != itemsSource2[i].RiverSta)
                    {
                        idx1 = Array.IndexOf(arr1, rsta);
                        idx2 = Array.IndexOf(arr2, rsta);

                        if (idx1 > 0 || idx2 > 0)
                        {

                            //System.Diagnostics.Debug.WriteLine($"rsta: {rsta}");
                            System.Diagnostics.Debug.WriteLine($"i: {idx1}");
                            System.Diagnostics.Debug.WriteLine($"idx: {idx2}");
                            //System.Diagnostics.Debug.WriteLine($"itemsSource1[i].RiverSta: {itemsSource1[idx1].RiverSta}");
                            //System.Diagnostics.Debug.WriteLine($"itemsSource1[idx].RiverSta: {itemsSource2[idx2].RiverSta}");
                            //System.Diagnostics.Debug.WriteLine($"zzzzzzzzz");
                        }
                    }

                    if (idx1 > 0 && idx2 > 0)
                    {
                        wselsub = itemsSource2[idx2].WSElev - itemsSource1[idx1].WSElev;

                     }
                    else
                    {
                        wselsub = 0;
                        System.Diagnostics.Debug.WriteLine($"itemsSource1[i].RiverSta: {idx1}");
                        System.Diagnostics.Debug.WriteLine($"itemsSource1[i].RiverSta: {itemsSource1[i].RiverSta}");
                        System.Diagnostics.Debug.WriteLine($"itemsSource1[idx].RiverSta: {idx2}");
                        System.Diagnostics.Debug.WriteLine($"itemsSource1[i].RiverSta: {itemsSource2[i].RiverSta}");
                        System.Diagnostics.Debug.WriteLine($" ");
                    }
                    results.Add(new HecRasProfileWselResult
                        {
                            RiverSta = rsta,
                            WSElev = Math.Round(wselsub, 2)
                            //WSElev = itemsSource2[i].WSElev - itemsSource1[i].WSElev,
                        });
                    }
                } else {
                    for (int i = 0; i < resN; i++)
                    {
                        results.Add(new HecRasProfileWselResult
                        {
                            RiverSta = itemsSource1[i].RiverSta,
                            //WSElev = itemsSource1[i].WSElev,
                            WSElev = Math.Round(itemsSource2[i].WSElev - itemsSource1[i].WSElev, 2),
                        });
                    }
                }
            return results;
        }
    }











    public class LoadCSV
    {
        public static DataView GetCsvData(string path)
        {
            string filePath = "C:/Temp/zzzzzzzzzz.csv";

            DataTable dataTable = new DataTable();
            TextFieldParser parser = new TextFieldParser(path);

            parser.SetDelimiters(",");

            if (parser.EndOfData)
            {
                var columns = parser.ReadFields();
                foreach (var col in columns)
                {
                    dataTable.Columns.Add(col);

                }
            }

            while (!parser.EndOfData)
            {
                var row = parser.ReadFields();
                dataTable.Rows.Add(row);
            }

            return dataTable.DefaultView;
        }
    }






    public class LoadHDF
    {
        public string Reach { get; set; }

        public static void MainLoadHDF()
        {
            string filePath = "C:/Temp/TAN_Main_SG.p01.hdf";
            try
            {
                using var file = H5File.OpenRead(filePath);
                var group = file.Group("Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections");

                var dataset = group.Dataset("/Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Water Surface");

                float[,] dataArray = dataset.Read<float[,]>();

                List<List<float>> listOfLists = [];
                int rows = dataArray.GetLength(0);
                int cols = dataArray.GetLength(1);

                List<float> rowX = [];

                for (int j = 0; j < cols; j++)
                {
                    rowX.Add(dataArray[5, j]);
                }
                listOfLists.Add(rowX);

                System.Diagnostics.Debug.WriteLine($"Attribute value: {group.Name}");
                System.Diagnostics.Debug.WriteLine("ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ: ");
            }
            catch (IOException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error accessing file: {ex.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"An error occurred: {ex.Message}");
            }
        }
    }



    public class HecRasProfileResult : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public string River { get; set; }
        public string Reach { get; set; }
        public string RiverSta { get; set; }
        public string Profile { get; set; }
        public float QTotal { get; set; }
        public float MinChEl { get; set; }
        public double WSElev { get; set; }
        public double MaxDepths { get; set; }
        public double CritWS { get; set; }
        public double EGElev { get; set; }
        public double EGSlope { get; set; }
        public double VelChnl { get; set; }
        public double FlowArea { get; set; }
        public double TopWidth { get; set; }
        public double FrChnl { get; set; }
    }



    public class HecRasProfileWselResult : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public string RiverSta { get; set; }
        public double WSElev { get; set; }

    }




}