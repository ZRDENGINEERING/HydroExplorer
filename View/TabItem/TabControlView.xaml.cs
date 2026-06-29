using HydroExplorer.ViewModel.TabItem;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Controls;

namespace HydroExplorer.View.TabItem
{
    public partial class TabControlView : UserControl
    {
        public TabControlView()
        {
            InitializeComponent();
            DataContext = App.ServiceProvider.GetRequiredService<TabControlViewModel>();

        }
    }
}
