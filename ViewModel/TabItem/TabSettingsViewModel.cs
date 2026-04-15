using HydroExplorer.Helpers;



namespace HydroExplorer.ViewModel.TabItem
{
    internal class TabSettingsViewModel : TabViewModelBase
    {
        private string _header = "Settings";
        public override string Header
        {
            get => _header;
            set { _header = value; OnPropertyChanged(nameof(Header)); }
        }


        public TabSettingsViewModel(IUserSettingsRepo settingsRepo)
        {
            SettingsRepo = settingsRepo;
            _ = LoadRecentProjectsAsync();

            EventBus.ProjPathChanged += async _ => await LoadRecentProjectsAsync();
        }
    }
}