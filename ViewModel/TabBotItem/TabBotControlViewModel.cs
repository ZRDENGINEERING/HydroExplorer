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
            _chartTab = new TabBotItemViewModel { Header = "Charts", Content = null };

            Tabs =
            [
                _chartTab,
            new TabBotItemViewModel { Header = "Output Window", Content = new TabBotOutputViewModel() },
        ];

            SelectedTab = Tabs[0];

            //System.Diagnostics.Debug.WriteLine(
            //    $"TabBotControlViewModel: constructed. _chartTab.Header='{_chartTab.Header}', Content is null={_chartTab.Content == null}");

            EventBus.TopTabChanged += OnTopTabChanged;
        }




        private void OnTopTabChanged(string topTabHeader)
        {
            //System.Diagnostics.Debug.WriteLine($"TabBotControlViewModel.OnTopTabChanged: received '{topTabHeader}'");

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                // The actual top-tab name for the charts area is "Info" (confirmed
                // via runtime debug output — EventBus.TopTabChanged never fires
                // "Charts" in this build). TabBotChartsViewModel itself already
                // treats both "Charts" and "Info" as valid (see its ShowLP3Plot
                // logic) — this check previously only matched "Charts", so
                // _chartTab.Content was never populated and TabBotChartsView was
                // never constructed at all.
                bool isChartsTab = topTabHeader == "HMS Charts" || topTabHeader == "Info";

                if (isChartsTab)
                {
                    // Show Chart tab with real content
                    if (_chartTab!.Content == null)
                    {
                        _chartTab.Content = new TabBotChartsViewModel();
                        System.Diagnostics.Debug.WriteLine(
                            "TabBotControlViewModel.OnTopTabChanged: created new TabBotChartsViewModel for _chartTab.Content.");
                    }
                    else
                    {
                        //System.Diagnostics.Debug.WriteLine(
                        //    "TabBotControlViewModel.OnTopTabChanged: _chartTab.Content already set, reusing.");
                    }
                    _chartTab.Header = "Chart";
                }
                else
                {
                    //System.Diagnostics.Debug.WriteLine(
                    //    $"TabBotControlViewModel.OnTopTabChanged: '{topTabHeader}' not a charts-area tab — clearing _chartTab.Content (was null={_chartTab!.Content == null}).");
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