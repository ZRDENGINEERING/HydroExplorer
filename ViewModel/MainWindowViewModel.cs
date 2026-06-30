using HydroExplorer.Core;
using HydroExplorer.Helpers;
using HydroExplorer.ViewModel.TabItem;
using Microsoft.Extensions.DependencyInjection;

namespace HydroExplorer.ViewModel
{
    public class MainWindowViewModel : BaseViewModel
    {
        public HomeViewModel HomeVM { get; set; }
        public DiscoveryViewModel DiscoveryVM { get; set; }
        public DataGridViewModel DataGridVM { get; set; }
        public MapViewModel MapVM { get; set; }
        public TabInfoViewModel TabInfoVM { get; set; }

        public object _currentView;
        public object CurrentView
        {
            get => _currentView;
            set { _currentView = value; OnPropertyChanged(); }
        }

        public RelayCommand HomeViewCommand { get; set; }
        public RelayCommand DiscoveryViewCommand { get; set; }
        public RelayCommand DataGridViewCommand { get; set; }
        public RelayCommand MapViewCommand { get; set; }

        private readonly IUserSettingsRepo _settingsRepo;
        private readonly MapStateService _mapState;

        // Gates MapViewCommand — true once geometry export (XS.shp/River.shp) has
        // completed for the most recently published project, so MapView never reads
        // those files off disk before GeometryExportCoordinator has written them.
        // Starts true so the Map tab isn't permanently stuck disabled if no project
        // has been loaded yet (nothing to export, nothing to block on).
        private bool _isGeometryReady = true;
        public bool IsGeometryReady
        {
            get => _isGeometryReady;
            private set
            {
                if (_isGeometryReady == value) return;
                _isGeometryReady = value;
                OnPropertyChanged();
                // RelayCommand.CanExecuteChanged is wired to CommandManager.RequerySuggested,
                // not a private backing event — there's no instance method to raise it on.
                // This forces WPF to requery all commands' CanExecute, including
                // MapViewCommand, right when the gate flips.
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }

        // Guards against overlapping export runs if PathsReady fires again
        // (e.g. rapid project switching) before the previous export finished.
        private string? _exportInFlightForProjPath;

        public MainWindowViewModel(IUserSettingsRepo settingsRepo)
        {
            TabInfoVM = App.ServiceProvider.GetRequiredService<TabInfoViewModel>();

            _settingsRepo = settingsRepo;
            _mapState = App.ServiceProvider.GetRequiredService<MapStateService>();

            HomeVM = new HomeViewModel();
            DiscoveryVM = new DiscoveryViewModel();
            DataGridVM = new DataGridViewModel();
            MapVM = new MapViewModel();

            CurrentView = HomeVM;

            HomeViewCommand = new RelayCommand(o => { CurrentView = HomeVM; }, o => true);
            DiscoveryViewCommand = new RelayCommand(o => { CurrentView = DiscoveryVM; }, o => true);
            DataGridViewCommand = new RelayCommand(o => { CurrentView = DataGridVM; }, o => true);
            MapViewCommand = new RelayCommand(
                o => { CurrentView = MapVM; },
                o => IsGeometryReady);

            // Subscribe and replay LAST — same pattern as MapView's own subscription
            // to MapStateService, so a project published before this VM was
            // constructed isn't missed.
            _mapState.PathsReady += OnPathsReady;
            if (_mapState.CurrentPaths != null)
                OnPathsReady(_mapState.CurrentPaths);

            // Publish AppLoaded after all VMs are constructed so pane subscribers are ready
            _ = PublishAppLoadedAsync();
        }

        private void OnPathsReady(ProjectPaths paths)
        {
            _ = RunGeometryExportAsync(paths);
        }

        /// <summary>
        /// Runs XS/River geometry export for the given project before the Map tab is
        /// made navigable. Blocks MapViewCommand (via IsGeometryReady) for the
        /// duration so a user can't switch to MapView and read XS.shp/River.shp
        /// before GeometryExportCoordinator has finished writing them — this is the
        /// fix for the export-vs-tab-switch race that previously lived inside
        /// MapView itself.
        /// </summary>
        private async Task RunGeometryExportAsync(ProjectPaths paths)
        {
            if (string.IsNullOrEmpty(paths.ProjPath)) return;

            // Don't re-run for a project we're already exporting for.
            if (_exportInFlightForProjPath == paths.ProjPath) return;
            _exportInFlightForProjPath = paths.ProjPath;

            IsGeometryReady = false;

            try
            {
                string projKey = PathHelpers.NormalizeProjKey(paths.ProjPath);

                await GeometryExportCoordinator.ExportAllAsync(
                    _settingsRepo,
                    projKey,
                    pathHdfA: paths.PathHdfA,
                    pathHdfB: paths.PathHdfB,
                    pathXS: paths.PathXS,
                    pathRiver: paths.PathRiver);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MainWindowViewModel.RunGeometryExportAsync error: {ex.Message}");
            }
            finally
            {
                // Only clear the gate if a newer project hasn't already superseded
                // this run — avoids briefly flashing "ready" for a stale project.
                if (_exportInFlightForProjPath == paths.ProjPath)
                {
                    IsGeometryReady = true;
                    _exportInFlightForProjPath = null;
                }
            }
        }

        private async Task PublishAppLoadedAsync()
        {
            try
            {
                await Task.Delay(1500);

                var settings = await _settingsRepo.GetSettingsFresh();
                EventBus.PublishAppLoaded(settings);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MainWindowViewModel.PublishAppLoadedAsync error: {ex.Message}");
            }
        }

        public string _selectedImagePath;
        public string SelectedImagePath
        {
            get => _selectedImagePath;
            set { _selectedImagePath = value; OnPropertyChanged(); }
        }
    }
}