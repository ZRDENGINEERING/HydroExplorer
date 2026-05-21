using HydroExplorer.Helpers;
using System.Collections.ObjectModel;


namespace HydroExplorer.ViewModel.TabItem
{
    public class TabControlViewModel : TabViewModelBase
    {
        private string _header = "Tabs";
        public override string Header
        {
            get => _header;
            set { _header = value; OnPropertyChanged(); }
        }

        public ObservableCollection<TabItemViewModel> Tabs { get; set; }
        public static string CurrentTopTab { get; private set; } = "Info";


        private TabItemViewModel _selectedTab;
        public TabItemViewModel SelectedTab
        {
            get => _selectedTab;
            set
            {
                if (value?.Header == "Charts" && value.Content == null)
                    value.Content = new TabChartViewModel();

                _selectedTab = value;
                OnPropertyChanged();

                if (value?.Header != null)
                {
                    CurrentTopTab = value.Header;
                    EventBus.RaiseTopTabChanged(value.Header);
                }
            }
        }



        public TabControlViewModel()
        {
            var settingsRepo = new FileSystemUserSettingsRepo();

            Tabs =
            [
                new TabItemViewModel { Header = "Info",    Content = new TabInfoViewModel(settingsRepo) },
                new TabItemViewModel { Header = "Main",    Content = new TabMainViewModel() },
                new TabItemViewModel { Header = "Charts",  Content = null },
                new TabItemViewModel { Header = "Output",  Content = new TabSettingsViewModel() },
                new TabItemViewModel { Header = "Publish", Content = new TabPrintViewModel() },
            ];

            SelectedTab = Tabs[0];
            EventBus.RaiseTopTabChanged("Info");

        }
    }
}