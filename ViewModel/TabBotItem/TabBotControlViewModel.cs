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
            //System.Diagnostics.Debug.WriteLine("TabBotControlViewModel CONSTRUCTED");



            _chartTab = new TabBotItemViewModel { Header = "Charts", Content = null };

            Tabs =
            [
                _chartTab,
            new TabBotItemViewModel { Header = "Output Window", Content = new TabBotOutputViewModel() },
        ];

            SelectedTab = Tabs[0];

            EventBus.TopTabChanged += OnTopTabChanged;

            //System.Diagnostics.Debug.WriteLine($"TabBotControlViewModel — instance {GetHashCode()} subscribed to TopTabChanged");

        }




        private void OnTopTabChanged(string topTabHeader)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                bool isChartsTab = topTabHeader == "HMS Charts" || topTabHeader == "Info";

                if (isChartsTab)
                {
                    if (_chartTab!.Content == null)
                    {
                        _chartTab.Content = new TabBotChartsViewModel();
                        System.Diagnostics.Debug.WriteLine("  created TabBotChartsViewModel");
                    }
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
                // _chartTab.Header starts as "Charts" and is only renamed to "Chart"
                // inside OnTopTabChanged's first successful trigger — match both so
                // this fallback works regardless of which state Header is currently in.
                if ((value?.Header == "Chart" || value?.Header == "Charts") && value.Content == null)
                {
                    value.Content = new TabBotChartsViewModel();
                    //System.Diagnostics.Debug.WriteLine(
                    //    $"TabBotControlViewModel.SelectedTab setter: created new TabBotChartsViewModel (Header='{value.Header}').");
                }

                _selectedTab = value;
                OnPropertyChanged();
            }
        }
    }
}