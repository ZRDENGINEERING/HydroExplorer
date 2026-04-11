using HydroExplorer.ViewModel;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Controls;



namespace HydroExplorer.View.TabBotItem
{
    public partial class TabBotChartsView : UserControl
    {
        public PlotViewModel PlotVm { get; }

        public TabBotChartsView()
        {
            PlotVm = App.ServiceProvider.GetRequiredService<PlotViewModel>();
            DataContext = this;

            InitializeComponent();

            Loaded += async (s, e) =>
            {
                await PlotVm.LoadDataAsync();
                PlotVm.PlotModel?.InvalidatePlot(true);
            };
        }
    }
}