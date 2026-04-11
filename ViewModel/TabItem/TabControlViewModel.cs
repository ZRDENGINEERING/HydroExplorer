using HydroExplorer.View.TabItem;
using System.Collections.ObjectModel;
using System.ComponentModel;


namespace HydroExplorer.ViewModel.TabItem
{
    public class TabControlViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public ObservableCollection<TabItemViewModel> Tabs { get; set; }

        private TabItemViewModel _selectedTab;
        public TabItemViewModel SelectedTab
        {
            get => _selectedTab;
            set { _selectedTab = value; OnPropertyChanged(nameof(SelectedTab)); }
        }

        public TabControlViewModel()
        {
            Tabs = new ObservableCollection<TabItemViewModel>
        {
            new TabItemViewModel { Header = "Info", Content = new TabInfoViewModel() },
            new TabItemViewModel { Header = "Main", Content = new TabMainViewModel() },
            new TabItemViewModel { Header = "Charts", Content = new TabChartViewModel() },
            new TabItemViewModel { Header = "Markup & Printing", Content = new TabPrintViewModel() },
            new TabItemViewModel { Header = "Settings", Content = new TabSettingsViewModel() }
        };

            SelectedTab = Tabs[0];
        }
    }
}