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

            Loaded += (s, e) => BuildColumnContextMenu();


            EventBus.ProfileChanged += OnProfileChanged;
            Unloaded += (s, e) =>
            {
                EventBus.ProjPathChanged -= OnProjPathChanged;
                EventBus.HdfPathChanged -= OnHdfPathChanged;
                EventBus.ProfileChanged -= OnProfileChanged;
            };




            EventBus.HdfFileASelected += (_, name) => Dispatcher.Invoke(() =>
            {
                planNameA = name;
                UpdateColumnHeaders();
            });

            EventBus.HdfFileBSelected += (_, name) => Dispatcher.Invoke(() =>
            {
                planNameB = name;
                UpdateColumnHeaders();
            });

        }


        private void BuildColumnContextMenu()
        {
            var menu = new ContextMenu();

            foreach (var column in dgSimple.Columns)
            {
                var header = column.Header?.ToString() ?? "(column)";
                var item = new MenuItem
                {
                    Header = header,
                    IsCheckable = true,
                    IsChecked = true,
                    Tag = column
                };
                item.Click += ColumnMenuItem_Click;
                menu.Items.Add(item);
            }

            dgSimple.ContextMenu = menu;
        }

        private void ColumnMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem) return;
            if (menuItem.Tag is not DataGridColumn column) return;

            column.Visibility = menuItem.IsChecked
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private async void OnHdfPathChanged()
        {
            await Dispatcher.InvokeAsync(async () => await LoadDataGrid(fresh: true));
        }


        private async Task LoadDataGrid(bool fresh = false, string? overrideProName = null)
        {
            try
            {
                await LoadSettingsDataGrid(fresh);
                UpdateColumnHeaders();

                if (overrideProName != null)
                    proName = overrideProName;

                if (string.IsNullOrEmpty(hdfPathA) || string.IsNullOrEmpty(proName))
                {
                    System.Diagnostics.Debug.WriteLine("LoadDataGrid: HDF path A not set, skipping.");
                    return;
                }

                List<WSELTableOxy>? wselDataOxy;

                bool hasBothSources = !string.IsNullOrEmpty(hdfPathB);

                if (hasBothSources)
                {
                    wselDataOxy = HecRasHdfReader.ReadWSELTableOxy(hdfPathA, hdfPathB, proName);
                }
                else
                {
                    wselDataOxy = HecRasHdfReader.ReadWSELTableOxySingle(hdfPathA, proName);
                }


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

            projPath = settings.ProjPath;
            projDir = settings.ProjDir;

            if (string.IsNullOrEmpty(projPath)) return;
            if (!settings.Projects.TryGetValue(projPath, out var project)) return;

            hdfPathA = project.HdfPathA;
            hdfPathB = project.HdfPathB;
            planNameA = project.PlanNameA;
            planNameB = project.PlanNameB;
            proName = project.ProName;
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

        private void UpdateColumnHeaders()
        {
            var nameA = string.IsNullOrEmpty(planNameA) ? "HDF A" : planNameA;
            var nameB = string.IsNullOrEmpty(planNameB) ? "HDF B" : planNameB;

            foreach (var col in dgSimple.Columns)
            {
                var h = col.Header?.ToString() ?? "";
                if (h == "QTotal A" || h.StartsWith("QTotal (") && h.Contains(nameA))
                    col.Header = $"QTotal ({nameA})";
                else if (h == "WSElev A" || h.StartsWith("WSElev (") && h.Contains(nameA))
                    col.Header = $"WSElev ({nameA})";
                else if (h == "QTotal B" || h.StartsWith("QTotal (") && h.Contains(nameB))
                    col.Header = $"QTotal ({nameB})";
                else if (h == "WSElev B" || h.StartsWith("WSElev (") && h.Contains(nameB))
                    col.Header = $"WSElev ({nameB})";
                //else if (h == "DELTA" || h.StartsWith("WSElev (") && h.Contains(nameB))
                    //col.Header = $"DELTA ({nameB} - {nameA})";
            }
        }

        private async void OnProfileChanged(string profileName)
        {
            await Dispatcher.InvokeAsync(async () => await LoadDataGrid(fresh: false, overrideProName: profileName));
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
        public float QTotalA { get; set; }
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

        public string QTotalBDisplay => float.IsNaN(QTotalB) ? "—" : QTotalB.ToString("F0");
        public string WSElevBDisplay => double.IsNaN(WSElevB) ? "—" : WSElevB.ToString("F2");
        public string DELTADisplay => double.IsNaN(DELTA) ? "—" : DELTA.ToString("F2");
    }



}