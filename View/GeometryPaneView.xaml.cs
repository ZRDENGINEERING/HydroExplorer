using HydroExplorer.Helpers;
using System.Windows;
using System.Windows.Controls;

namespace HydroExplorer.View
{
    public partial class GeometryPaneView : UserControl
    {
        public GeometryPaneView()
        {
            InitializeComponent();
        }

        private void TreeViewItem_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
            => e.Handled = true;
    }
}
