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

        public PlotViewModel PlotVm { get; }

        public CreagerPlotViewModel CreagerVm { get; }

        public LP3PlotViewModel LP3Vm { get; }
        public LP3PlotViewModel RtnVm { get; }

        public OmegaPlotViewModel OmegaVm { get; }

        private bool _showProfilePlot = true;
        public bool ShowProfilePlot
        {
            get => _showProfilePlot;
            set { _showProfilePlot = value; OnPropertyChanged(); }
        }


        // LP3 stays on the HMS Charts bottom tab only — no longer shown on Info.
        private bool _showLP3Plot = true;
        public bool ShowLP3Plot
        {
            get => _showLP3Plot;
            set { _showLP3Plot = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowChartsRightPane)); }
        }


        private bool _showReturnPlot = true;
        public bool ShowReturnPlot
        {
            get => _showReturnPlot;
            set { _showReturnPlot = value; OnPropertyChanged(); }
        }

        // Omega EM plot — shown only on the Info tab's bottom chart (right side).
        private bool _showOmegaPlot = false;
        public bool ShowOmegaPlot
        {
            get => _showOmegaPlot;
            set { _showOmegaPlot = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowChartsRightPane)); }
        }

        // True whenever either right-side plot (LP3 on HMS Charts, Omega on
        // Info) is active — drives the shared Creager pane + splitter, which
        // sit to the left of whichever one is currently showing.
        public bool ShowChartsRightPane => ShowLP3Plot || ShowOmegaPlot;

        // Latest WSEL data + selected profile, cached so the Creager point can be
        // recomputed without re-reading the HDF every time the profile changes.
        private List<WSELTableOxy>? _wselData;
        private string? _selectedProfile;

        public TabBotChartsViewModel()
        {
            PlotVm = App.ServiceProvider.GetRequiredService<PlotViewModel>();
            CreagerVm = new CreagerPlotViewModel();
            LP3Vm = new LP3PlotViewModel();
            OmegaVm = new OmegaPlotViewModel();


            var current = TabControlViewModel.CurrentTopTab;
            ShowProfilePlot = current == "RAS Tables";
            // LP3 lives on HMS Charts only now — Info gets Omega instead.
            ShowLP3Plot = current == "HMS Charts";
            ShowOmegaPlot = current == "Info";

            EventBus.TopTabChanged += topTab =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    ShowProfilePlot = topTab == "RAS Tables";
                    ShowLP3Plot = topTab == "HMS Charts";
                    ShowOmegaPlot = topTab == "Info";
                });
            };

            // Profile selection drives which cross-section's QTotalA feeds the
            // Creager point (most downstream station for that profile).
            EventBus.ProfileChanged += profile =>
            {
                _selectedProfile = profile;
                _ = CreagerVm.LoadDataAsync(_wselData, _selectedProfile);
            };

            // HdfPathChanged now carries the resolved paths directly. Use them
            // as-is (including "" for "no Plan B") instead of re-reading
            // UserSettings, which is debounced and can race ahead of the write.
            //
            // PlanNamesChanged is intentionally NOT subscribed here anymore —
            // HydraulicsPaneView always publishes it together with
            // HdfPathChanged, and its old handler re-read HdfPathA/HdfPathB
            // from settings, which could fire after HdfPathChanged's correct
            // reload and clobber it with stale data.
            EventBus.HdfPathChanged += async (hdfPathA, hdfPathB) =>
                await LoadWselDataAsync(hdfPathA, hdfPathB);

            EventBus.OmegaInputChanged += (area, slope, precip, omega) =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    OmegaVm.LoadData(area, slope, precip, omega));
            };

            _ = LoadWselDataAsync();
        }

        /// <summary>
        /// Reads WSELTableOxy for the active project's HDF path(s)/profile — used
        /// only to feed CreagerVm's peak-discharge point. PlotVm reads and manages
        /// its own data independently for the Main-tab profile chart.
        ///
        /// hdfPathA/hdfPathB, when supplied (non-null — "" is a valid "no plan"
        /// value, not "unset"), come from a live HdfPathChanged payload and take
        /// precedence over settings. Left null only for the constructor's
        /// initial load, where settings are the only source available yet.
        /// </summary>
        private async Task LoadWselDataAsync(string? hdfPathA = null, string? hdfPathB = null)
        {
            try
            {
                var settingsRepo = App.ServiceProvider.GetRequiredService<IUserSettingsRepo>();
                var settings = await settingsRepo.GetSettings();

                if (string.IsNullOrEmpty(settings.ProjPath) ||
                    !settings.Projects.TryGetValue(settings.ProjPath, out var proj))
                    return;

                string resolvedHdfPathA = hdfPathA ?? proj.HdfPathA;
                string resolvedHdfPathB = hdfPathB ?? proj.HdfPathB;
                string proName = !string.IsNullOrEmpty(_selectedProfile) ? _selectedProfile : proj.ProName;

                _wselData = HecRasHdfReader.ReadWSELTableOxy(resolvedHdfPathA, resolvedHdfPathB, proName, out string? profileWarning);

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