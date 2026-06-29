using System.Windows.Controls;

namespace HydroExplorer.View.TabItem
{
    public partial class TabInfoView : UserControl
    {
        private static MapOverView? _mapOverView;

        public TabInfoView()
        {
            InitializeComponent();

            if (_mapOverView == null)
                _mapOverView = new MapOverView();

            MapContainer.Content = _mapOverView;
        }
    }
}