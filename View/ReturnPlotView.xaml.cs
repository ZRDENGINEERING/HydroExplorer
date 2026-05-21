using HydroExplorer.ViewModel;
using System.Windows.Controls;

namespace HydroExplorer.View
{
    public partial class ReturnPlotView : UserControl
    {
        public ReturnPlotView()
        {
            InitializeComponent();
            DataContext = new ReturnPlotViewModel();
        }
    }
}