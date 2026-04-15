using HydroExplorer.Helpers;
using HydroExplorer.Utils;
using HydroExplorer.ViewModel;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;



namespace HydroExplorer.View
{
    public partial class DataGridView : UserControl
    {
        private IUserSettingsRepo? _settingsRepo;
        public Dictionary<string, ProjectSettings> ProjSettings { get; set; } = [];
        public PlotViewModel PlotVm { get; } = new PlotViewModel();
        private readonly SelectionViewModel _selectionVm;


        public string? projDir = string.Empty;
        public string? projPath = string.Empty;
        public string? hdfPathA = string.Empty;
        public string? hdfPathB = string.Empty;
        public string? planNameA = string.Empty;
        public string? planNameB = string.Empty;
        public string? proName = string.Empty;

        public string filePath = string.Empty;

        private readonly List<HecRasProfileWselResult> itemsSourceA = [];
        private readonly List<HecRasProfileWselResult> itemsSourceB = [];



        public DataGridView()
        {
            InitializeComponent();

            _selectionVm = App.ServiceProvider.GetRequiredService<SelectionViewModel>();
            _selectionVm.PropertyChanged += OnSelectionChanged;

            Loaded += async (s, e) => await LoadDataGrid(fresh: true);
            IsVisibleChanged += async (s, e) =>
            {
                if ((bool)e.NewValue)
                    await LoadDataGrid(fresh: true);
            };


            EventBus.HdfPathChanged += OnHdfPathChanged;
            Unloaded += (s, e) =>
            {
                EventBus.ProjPathChanged -= OnProjPathChanged;
                EventBus.HdfPathChanged -= OnHdfPathChanged;
            };
        }

        private async void OnHdfPathChanged()
        {
            System.Diagnostics.Debug.WriteLine("OnHdfPathChanged fired");
            await Dispatcher.InvokeAsync(async () => await LoadDataGrid(fresh: true));
        }


        private async Task LoadDataGrid(bool fresh = false)
        {

            System.Diagnostics.Debug.WriteLine($"LoadDataGrid called, fresh={fresh}");
            try
            {
                await LoadSettingsDataGrid(fresh);

                if (string.IsNullOrEmpty(hdfPathA) || string.IsNullOrEmpty(hdfPathB))
                {
                    System.Diagnostics.Debug.WriteLine("LoadDataGrid: HDF paths not set, skipping.");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"LoadDataGrid: reading WSEL, proName={proName}");
                var wselDataOxy = HecRasHdfReader.ReadWSELTableOxy(hdfPathA, hdfPathB, proName);
                System.Diagnostics.Debug.WriteLine($"LoadDataGrid: got {wselDataOxy?.Count} rows");

                dgSimple.ItemsSource = wselDataOxy;
                PlotVm.LoadFromWSELTable(wselDataOxy);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadDataGrid error: {ex.Message}");
            }
        }

        private async void OnProjPathChanged(string path)
        {
            System.Diagnostics.Debug.WriteLine($"OnProjPathChanged fired: {path}");
            await Dispatcher.InvokeAsync(async () => await LoadDataGrid(fresh: true));
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



        private async Task LoadSettingsDataGrid(bool fresh = false)
        {
            _settingsRepo = App.ServiceProvider.GetRequiredService<IUserSettingsRepo>();
            var settings = fresh
                ? await _settingsRepo.GetSettingsFresh()
                : await _settingsRepo.GetSettings();

            System.Diagnostics.Debug.WriteLine($"LoadSettingsDataGrid: fresh={fresh}, projPath={settings.ProjPath}");


            projPath = settings.ProjPath;
            projDir = settings.ProjDir;

            if (string.IsNullOrEmpty(projPath)) return;
            if (!settings.Projects.TryGetValue(projPath, out var project)) return;

            hdfPathA = project.HdfPathA;
            hdfPathB = project.HdfPathB;
            planNameA = project.PlanNameA;
            planNameB = project.PlanNameB;
            proName = project.ProName;

            System.Diagnostics.Debug.WriteLine($"LoadSettingsDataGrid: projPath={projPath}, hdfA={hdfPathA}, proName={proName}");
        }


        public static HecRasProfileWselResult GetStaInfo(List<HecRasProfileWselResult> calcComp)
        {
            HecRasProfileWselResult result = calcComp.ElementAt(11);
            return result;
        }


        public List<HecRasProfileWselResult> CalcCompare()
        {
            int resN1 = itemsSourceA.Count;
            int resN2 = itemsSourceB.Count;
            string rsta;
            int resN = Math.Min(resN1, resN2);
            var results = new List<HecRasProfileWselResult>(resN);

            string[] arr1 = new string[resN];
            string[] arr2 = new string[resN];

            if (resN1 != resN2)
            {
                for (int i = 0; i < resN; i++)
                {
                    arr1[i] = itemsSourceA[i].RiverSta;
                    arr2[i] = itemsSourceB[i].RiverSta;
                }


                for (int i = 0; i < resN; i++)
                {
                    int idx1 = i;
                    int idx2 = i;
                    rsta = arr1[i];

                    if (resN1 > resN2)
                        rsta = arr2[i];

                    if (itemsSourceA[i].RiverSta != itemsSourceB[i].RiverSta)
                    {
                        idx1 = Array.IndexOf(arr1, rsta);
                        idx2 = Array.IndexOf(arr2, rsta);
                    }

                    double wselsub;
                    if (idx1 > 0 && idx2 > 0)
                    {
                        wselsub = itemsSourceB[idx2].WSElev - itemsSourceA[idx1].WSElev;

                    }
                    else
                    {
                        wselsub = 0;
                    }
                    results.Add(new HecRasProfileWselResult
                    {
                        RiverSta = rsta,
                        WSElev = Math.Round(wselsub, 2),
                        DELTA = Math.Round(wselsub, 2)
                    });
                }
            }
            else
            {
                for (int i = 0; i < resN; i++)
                {
                    results.Add(new HecRasProfileWselResult
                    {
                        RiverSta = itemsSourceA[i].RiverSta,
                        WSElev = Math.Round(itemsSourceB[i].WSElev - itemsSourceA[i].WSElev, 2),
                    });
                }
            }
            return results;
        }

        private void Click_Me(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            string str = btn.Content.ToString() + " button clicked";
            MessageBox.Show(str);
        }




        private void DgSimpleSlctChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgSimple.SelectedItem is WSELTableOxy row)
            {
                _selectionVm.SelectedRiverSta = row.RiverSta;
                _selectionVm.SelectedDelta = row.DELTA;
            }
        }



    }





    public class HecRasProfileResult : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
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
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        public string RiverSta { get; set; }
        public double WSElev { get; set; }
        public double DELTA { get; set; }
    }



    public class WSELTable : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
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
        public event PropertyChangedEventHandler? PropertyChanged;
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