using HydroExplorer.ViewModel;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace HydroExplorer.View
{
    public partial class PlotWindow : Window
    {
        private readonly PlotViewModel _vm;

        public PlotWindow()
        {
            InitializeComponent();
            _vm = App.ServiceProvider.GetRequiredService<PlotViewModel>();
            DataContext = _vm;
        }

        protected override async void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            await _vm.LoadDataAsync();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}