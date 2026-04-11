using HydroExplorer.Core;
using HydroExplorer.Helpers;




namespace HydroExplorer.ViewModel
{

    public class MainWindowViewModel : BaseViewModel
    {
        public HomeViewModel HomeVM { get; set; }
        public DiscoveryViewModel DiscoveryVM { get; set; }
        public DataGridViewModel DataGridVM { get; set; }
        public MapViewModel MapVM { get; set; }


        public object _currentView;

        public object CurrentView
        {
            get { return _currentView; }
            set
            {
                _currentView = value;
                OnPropertyChanged();
            }
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

            CurrentView = HomeVM;

            HomeViewCommand = new RelayCommand(o => { CurrentView = HomeVM; }, canExecute: o => true);
            DiscoveryViewCommand = new RelayCommand(o => { CurrentView = DiscoveryVM; }, canExecute: o => true);
            DataGridViewCommand = new RelayCommand(o => { CurrentView = DataGridVM; }, canExecute: o => true);
            MapViewCommand = new RelayCommand(o => { CurrentView = MapVM; }, canExecute: o => true);

        }
      

        public string _selectedImagePath;
        public string SelectedImagePath
        {
            get => _selectedImagePath;
            set { _selectedImagePath = value; OnPropertyChanged(); }
        }

    }
}
