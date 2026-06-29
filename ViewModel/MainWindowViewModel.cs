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

        public MainWindowViewModel(IUserSettingsRepo settingsRepo)
        {
            TabInfoVM = App.ServiceProvider.GetRequiredService<TabInfoViewModel>();

            _settingsRepo = settingsRepo;

            HomeVM = new HomeViewModel();
            DiscoveryVM = new DiscoveryViewModel();
            DataGridVM = new DataGridViewModel();
            MapVM = new MapViewModel();

            CurrentView = HomeVM;

            HomeViewCommand = new RelayCommand(o => { CurrentView = HomeVM; }, o => true);
            DiscoveryViewCommand = new RelayCommand(o => { CurrentView = DiscoveryVM; }, o => true);
            DataGridViewCommand = new RelayCommand(o => { CurrentView = DataGridVM; }, o => true);
            MapViewCommand = new RelayCommand(o => { CurrentView = MapVM; }, o => true);

            // Publish AppLoaded after all VMs are constructed so pane subscribers are ready
            _ = PublishAppLoadedAsync();
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
