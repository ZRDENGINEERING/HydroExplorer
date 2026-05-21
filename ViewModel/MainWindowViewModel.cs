using HydroExplorer.Core;
using HydroExplorer.Helpers;
using HydroExplorer.ViewModel.TabItem;

namespace HydroExplorer.ViewModel
{
    public class MainWindowViewModel : BaseViewModel
    {
        public HomeViewModel HomeVM { get; set; }
        public DiscoveryViewModel DiscoveryVM { get; set; }
        public DataGridViewModel DataGridVM { get; set; }
        public MapViewModel MapVM { get; set; }
        public TabInfoViewModel TabInfoVM { get; set; }  // ← add this

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
            _settingsRepo = settingsRepo;

            HomeVM = new HomeViewModel();
            DiscoveryVM = new DiscoveryViewModel();
            DataGridVM = new DataGridViewModel();
            MapVM = new MapViewModel();
            TabInfoVM = new TabInfoViewModel(settingsRepo);  // ← add this

            CurrentView = HomeVM;

            HomeViewCommand = new RelayCommand(o => { CurrentView = HomeVM; }, o => true);
            DiscoveryViewCommand = new RelayCommand(o => { CurrentView = DiscoveryVM; }, o => true);
            DataGridViewCommand = new RelayCommand(o => { CurrentView = DataGridVM; }, o => true);
            MapViewCommand = new RelayCommand(o => { CurrentView = MapVM; }, o => true);
        }

        public string _selectedImagePath;
        public string SelectedImagePath
        {
            get => _selectedImagePath;
            set { _selectedImagePath = value; OnPropertyChanged(); }
        }
    }
}