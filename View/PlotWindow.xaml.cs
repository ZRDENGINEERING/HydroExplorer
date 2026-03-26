using HydroExplorer.ViewModel;
using System.Windows;


namespace HydroExplorer.View
{
    public partial class PlotWindow : Window
    {
        public PlotWindow()
        {
            InitializeComponent();
        }


        // Optional: open with specific data
        public PlotWindow(List<(double x, double y)> data)
        {
            InitializeComponent();
            (DataContext as PlotViewModel)?.UpdatePlot(data);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
            => Close();
    }
}