using HydroExplorer.Helpers;
using Microsoft.Extensions.DependencyInjection;
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
            var tabInfoVM = App.ServiceProvider.GetRequiredService<TabInfoViewModel>();

            Tabs =
            [
                new TabItemViewModel { Header = "Info",    Content = tabInfoVM },
                new TabItemViewModel { Header = "Main",    Content = new TabMainViewModel() },
                new TabItemViewModel { Header = "Charts",  Content = null },
                new TabItemViewModel { Header = "Publish", Content = new TabPublishViewModel() },
                new TabItemViewModel { Header = "BlankTop",  Content = new TabSettingsViewModel() }
            ];

            SelectedTab = Tabs[0];
            EventBus.RaiseTopTabChanged("Info");
        }
    }
}