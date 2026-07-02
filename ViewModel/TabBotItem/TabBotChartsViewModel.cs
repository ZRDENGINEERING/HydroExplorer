using HydroExplorer.Helpers;
using HydroExplorer.Utils;
using HydroExplorer.View;
using HydroExplorer.ViewModel.TabItem;
using Microsoft.Extensions.DependencyInjection;



namespace HydroExplorer.ViewModel.TabBotItem
{
    public class TabBotChartsViewModel : TabBotViewModelBase
    {
        private string _header = "Chart";
        public override string Header
        {
            get => _header;
            set { _header = value; OnPropertyChanged(nameof(Header)); }
        }

        // Main tab — original WSEL profile chart (unchanged).
        public PlotViewModel PlotVm { get; }

        // Charts/Info tab — Creager envelope curve, replacing what previously
        // occupied this slot.
        public CreagerPlotViewModel CreagerVm { get; }

        public LP3PlotViewModel LP3Vm { get; }
        public LP3PlotViewModel RtnVm { get; }

        private bool _showProfilePlot = true;
        public bool ShowProfilePlot
        {
            get => _showProfilePlot;
            set { _showProfilePlot = value; OnPropertyChanged(); }
        }


        private bool _showLP3Plot = true;
        public bool ShowLP3Plot
        {
            get => _showLP3Plot;
            set { _showLP3Plot = value; OnPropertyChanged(); }
        }


        private bool _showReturnPlot = true;
        public bool ShowReturnPlot
        {
            get => _showReturnPlot;
            set { _showReturnPlot = value; OnPropertyChanged(); }
        }

        // Latest WSEL data + selected profile, cached so the Creager point can be
        // recomputed without re-reading the HDF every time the profile changes.
        private List<WSELTableOxy>? _wselData;
        private string? _selectedProfile;

        public TabBotChartsViewModel()
        {
            PlotVm = App.ServiceProvider.GetRequiredService<PlotViewModel>();
            CreagerVm = new CreagerPlotViewModel();
            LP3Vm = new LP3PlotViewModel();


            var current = TabControlViewModel.CurrentTopTab;
            ShowProfilePlot = current == "Main";
            ShowLP3Plot = current == "Charts" || current == "Info";

            EventBus.TopTabChanged += topTab =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    ShowProfilePlot = topTab == "Main";
                    ShowLP3Plot = topTab == "Charts" || topTab == "Info";
                });
            };

            // Profile selection drives which cross-section's QTotalA feeds the
            // Creager point (most downstream station for that profile).
            EventBus.ProfileChanged += profile =>
            {
                _selectedProfile = profile;
                _ = CreagerVm.LoadDataAsync(_wselData, _selectedProfile);
            };

            // HDF path change means WselData is stale until it's re-read — re-read
            // here directly so Creager doesn't depend on PlotVm's own load cycle.
            EventBus.HdfPathChanged += async () => await LoadWselDataAsync();

            EventBus.PlanNamesChanged += async (planA, planB) => await LoadWselDataAsync();

            _ = LoadWselDataAsync();
        }

        /// <summary>
        /// Reads WSELTableOxy for the active project's HDF path(s)/profile — used
        /// only to feed CreagerVm's peak-discharge point. PlotVm reads and manages
        /// its own data independently for the Main-tab profile chart.
        /// </summary>
        private async Task LoadWselDataAsync()
        {
            try
            {
                var settingsRepo = App.ServiceProvider.GetRequiredService<IUserSettingsRepo>();
                var settings = await settingsRepo.GetSettings();

                if (string.IsNullOrEmpty(settings.ProjPath) ||
                    !settings.Projects.TryGetValue(settings.ProjPath, out var proj))
                    return;

                string proName = !string.IsNullOrEmpty(_selectedProfile) ? _selectedProfile : proj.ProName;

                _wselData = HecRasHdfReader.ReadWSELTableOxy(proj.HdfPathA, proj.HdfPathB, proName, out string? profileWarning);

                EventBus.PublishProfileMismatchWarning(profileWarning ?? string.Empty);

                await CreagerVm.LoadDataAsync(_wselData, proName);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TabBotChartsViewModel.LoadWselDataAsync error: {ex.Message}");
            }
        }
    }
}