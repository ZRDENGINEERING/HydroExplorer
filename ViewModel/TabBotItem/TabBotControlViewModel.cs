using HydroExplorer.Helpers;
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

        private TabBotItemViewModel? _chartTab;

        public TabBotControlViewModel()
        {
            _chartTab = new TabBotItemViewModel { Header = "Chart", Content = null };

            Tabs =
            [
                _chartTab,
            new TabBotItemViewModel { Header = "Output", Content = new TabBotOutputViewModel() },
        ];

            SelectedTab = Tabs[0];

            EventBus.TopTabChanged += OnTopTabChanged;
        }




        private void OnTopTabChanged(string topTabHeader)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (topTabHeader == "Charts")
                {
                    // Show Chart tab with real content
                    if (_chartTab!.Content == null)
                        _chartTab.Content = new TabBotChartsViewModel();
                    _chartTab.Header = "Chart";
                }
                else
                {
                    _chartTab!.Content = null;
                }
            });
        }

        private TabBotItemViewModel _selectedTab;
        public TabBotItemViewModel SelectedTab
        {
            get => _selectedTab;
            set
            {
                if (value?.Header == "Chart" && value.Content == null)
                    value.Content = new TabBotChartsViewModel();

                _selectedTab = value;
                OnPropertyChanged();
            }
        }
    }
}