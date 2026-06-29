using HydroExplorer.ViewModel.TabBotItem;
using System.Windows;
using System.Windows.Controls;


namespace HydroExplorer.View.TabBotItem
{
    public partial class TabBotChartsView : UserControl
    {
        public TabBotChartsView()
        {
            InitializeComponent();

            Loaded += async (s, e) =>
            {
                if (DataContext is TabBotChartsViewModel vm)
                {
                    await vm.PlotVm.LoadDataAsync();
                    vm.PlotVm.PlotModel?.InvalidatePlot(true);
                }
            };
        }

        
    }
}