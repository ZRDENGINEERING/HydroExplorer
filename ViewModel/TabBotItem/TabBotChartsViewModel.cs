using HydroExplorer.Helpers;
using HydroExplorer.ViewModel.TabItem;
using Microsoft.Extensions.DependencyInjection;



namespace HydroExplorer.ViewModel.TabBotItem
{
    public class TabBotChartsViewModel : TabBotViewModelBase
    {
        private string _header = "Chart";
        public override string Header
        {
            get => _header;
            set { _header = value; OnPropertyChanged(nameof(Header)); }
        }

        public PlotViewModel PlotVm { get; }
        public LP3PlotViewModel LP3Vm { get; }
        public LP3PlotViewModel RtnVm { get; }

        private bool _showProfilePlot = true;
        public bool ShowProfilePlot
        {
            get => _showProfilePlot;
            set { _showProfilePlot = value; OnPropertyChanged(); }
        }


        private bool _showLP3Plot = true;
        public bool ShowLP3Plot
        {
            get => _showLP3Plot;
            set { _showLP3Plot = value; OnPropertyChanged(); }
        }


        private bool _showReturnPlot = true;
        public bool ShowReturnPlot
        {
            get => _showReturnPlot;
            set { _showReturnPlot = value; OnPropertyChanged(); }
        }

        public TabBotChartsViewModel()
        {
            PlotVm = App.ServiceProvider.GetRequiredService<PlotViewModel>();
            LP3Vm = new LP3PlotViewModel();

            // Set initial state
            var current = TabControlViewModel.CurrentTopTab;
            ShowProfilePlot = current == "Main";
            ShowLP3Plot = current == "Charts" || current == "Info";

            EventBus.TopTabChanged += topTab =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    ShowProfilePlot = topTab == "Main";
                    ShowLP3Plot = topTab == "Charts" || topTab == "Info";
                });
            };
        }




    }
}