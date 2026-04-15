using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;



namespace HydroExplorer.ViewModel.TabBotItem
{

    public class TabBotControlViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public ObservableCollection<TabBotItemViewModel> Tabs { get; set; }

        public string Header { get; set; } = "Bottom";

        private TabBotItemViewModel _selectedTab;
        public TabBotItemViewModel SelectedTab
        {
            get => _selectedTab;
            set { _selectedTab = value; OnPropertyChanged(); }
        }

        public TabBotControlViewModel()
        {
            Tabs =
            [
                new TabBotItemViewModel { Header = "Chart", Content = new TabBotChartsViewModel() },
                new TabBotItemViewModel { Header = "Output", Content = new TabBotOutputViewModel() },
            ];

                SelectedTab = Tabs[0];
        }
    }
}