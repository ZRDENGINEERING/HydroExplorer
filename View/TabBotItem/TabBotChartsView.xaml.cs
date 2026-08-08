using HydroExplorer.ViewModel.TabBotItem;
using System.Windows;
using System.Windows.Controls;


namespace HydroExplorer.View.TabBotItem
{
    public partial class TabBotChartsView : UserControl
    {
        public static readonly DependencyProperty ViewModelProperty =
            DependencyProperty.Register(
                nameof(ViewModel),
                typeof(TabBotChartsViewModel),
                typeof(TabBotChartsView),
                new PropertyMetadata(null));

        public TabBotChartsViewModel? ViewModel
        {
            get => (TabBotChartsViewModel?)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }

        public TabBotChartsView()
        {
            InitializeComponent();
            
            DataContextChanged += (s, e) =>
            {
                if (e.NewValue is TabBotChartsViewModel vm)
                    ViewModel = vm;
            };

            Loaded += async (s, e) =>
            {
                if (ViewModel is { } vm)
                {
                    await vm.PlotVm.LoadDataAsync();
                    vm.PlotVm.PlotModel?.InvalidatePlot(true);
                    vm.CreagerVm.PlotModelChartsTab?.InvalidatePlot(true);
                }
            };
            
            Unloaded += (s, e) =>
            {
                ProfilePlotView.Model = null;
                CreagerPlotView.Model = null;
                LP3PlotView.Model = null;
            };
        }
        private void PlotView_Unloaded(object sender, RoutedEventArgs e)
        {
            if (sender is OxyPlot.Wpf.PlotView pv)
                pv.Model = null;
        }
    }
}