using HydroExplorer.Helpers;
using HydroExplorer.Utils;
using HydroExplorer.ViewModel;
using HydroExplorer.View;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualBasic.FileIO;
using System.ComponentModel;
using System.Data;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;



namespace HydroExplorer.View
{
    public partial class DataGridView : UserControl
    {
        private IUserSettingsRepo? _settingsRepo;
        public Dictionary<string, ProjectSettings> projSettings { get; set; } = new();
        public PlotViewModel PlotVm { get; } = new PlotViewModel();
        private SelectionViewModel _selectionVm;

        


        public string? projDir = string.Empty;
        public string? projPath = string.Empty;
        public string? hdfPathA = string.Empty;
        public string? hdfPathB = string.Empty;
        public string? planNameA = string.Empty;
        public string? planNameB = string.Empty;
        public string? proName = string.Empty;

        //public string filePath = "C:/Temp/TAN_Main_SG.p01.hdf";
        public string filePath = string.Empty;

        private readonly HecRasHdfReader _reader = new();

        private List<HecRasProfileWselResult> itemsSource1 = [];
        private List<HecRasProfileWselResult> itemsSource2 = [];

        private PlotViewModel _plotVm;


        public DataGridView()
        {
            InitializeComponent();

            _selectionVm = App.ServiceProvider.GetRequiredService<SelectionViewModel>();
            _selectionVm.PropertyChanged += OnSelectionChanged;


            Loaded += async (s, e) =>
            {
                await LoadSettingsDataGrid(); // fine here — this IS DataGridView

                _plotVm = App.ServiceProvider.GetRequiredService<PlotViewModel>();


                var wselDataOxy = _reader.ReadWSELTableOxy(hdfPathA, hdfPathB, proName);

                dgSimple.ItemsSource = wselDataOxy;
                PlotVm.LoadFromWSELTable(wselDataOxy);
            };
        }

        private void OnSelectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(SelectionViewModel.SelectedRiverSta)) return;

            var rs = _selectionVm.SelectedRiverSta;
            if (string.IsNullOrEmpty(rs)) return;

            var item = dgSimple.Items
                .OfType<WSELTableOxy>()
                .FirstOrDefault(x => x.RiverSta?.Trim() == rs.Trim());

            if (item == null) return;

            dgSimple.Dispatcher.Invoke(() =>
            {
                dgSimple.SelectedItem = item;
                dgSimple.ScrollIntoView(item);
            });
        }



        private async Task LoadSettingsDataGrid()
        {
            _settingsRepo = App.ServiceProvider.GetRequiredService<IUserSettingsRepo>();
            var settings = await _settingsRepo.GetSettings();

            projPath = settings.ProjPath;
            projDir = settings.ProjDir;

            var _projects = settings.Projects[projPath];

            hdfPathA = _projects.HdfPathA;
            hdfPathB = _projects.HdfPathB;
            planNameA = _projects.PlanNameA;
            planNameB = _projects.PlanNameB;
            proName = _projects.ProName;
        }


        public HecRasProfileWselResult getStaInfo(List<HecRasProfileWselResult> calcComp, string riverSta)
        {
            HecRasProfileWselResult result = calcComp.ElementAt(11);
            var tst = result.WSElev;


            //System.Diagnostics.Debug.WriteLine($"\n result.WSElev: {tst}");
            //System.Diagnostics.Debug.WriteLine($"result.RiverSta: {result.RiverSta} \n");
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
                            //System.Diagnostics.Debug.WriteLine($"i: {idx1}");
                            //System.Diagnostics.Debug.WriteLine($"idx: {idx2}");
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
                        //System.Diagnostics.Debug.WriteLine($"itemsSource1[i].RiverSta: {idx1}");
                        //System.Diagnostics.Debug.WriteLine($"itemsSource1[i].RiverSta: {itemsSource1[i].RiverSta}");
                        //System.Diagnostics.Debug.WriteLine($"itemsSource1[idx].RiverSta: {idx2}");
                        //System.Diagnostics.Debug.WriteLine($"itemsSource1[i].RiverSta: {itemsSource2[i].RiverSta}");
                        //System.Diagnostics.Debug.WriteLine($" ");
                    }
                    results.Add(new HecRasProfileWselResult
                    {
                        RiverSta = rsta,
                        WSElev = Math.Round(wselsub, 2),
                        DELTA = Math.Round(wselsub, 2)

                        //WSElev = itemsSource2[i].WSElev - itemsSource1[i].WSElev,
                    });
                }
            }
            else
            {
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
            //System.Diagnostics.Debug.WriteLine(results);
            return results;
        }

        private void Click_Me(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            string str = btn.Content.ToString() + " button clicked";
            MessageBox.Show(str);
        }




        private void dgSimple_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgSimple.SelectedItem is WSELTableOxy row) // ✅ changed to WSELTableOxy
            {
                _selectionVm.SelectedRiverSta = row.RiverSta;
                _selectionVm.SelectedDelta = row.DELTA;
            }
        }



    }


    public class LoadCSV
    {
        public static DataView GetCsvData(string path)
        {
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
                _ = dataTable.Rows.Add(values: row);
            }
            return dataTable.DefaultView;
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
        public double DELTA { get; set; }
    }



    public class WSELTable : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public string River { get; set; }
        public string Reach { get; set; }
        public string RiverSta { get; set; }
        public string Profile { get; set; }
        public float QTotal { get; set; }
        public double WSElevA { get; set; }
        public float QTotalB { get; set; }
        public double WSElevB { get; set; }
        public double DELTA { get; set; }

        //public double DELTA = res.DELTA;
    }


    public class WSELTableOxy : INotifyPropertyChanged
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
        public double WSElevA { get; set; }
        public float QTotalB { get; set; }
        public double WSElevB { get; set; }
        public double DELTA { get; set; }

        //public double DELTA = res.DELTA;
    }
}