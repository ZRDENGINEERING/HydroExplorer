using System.Windows.Controls;


namespace HydroExplorer.View.TabItem
{
    public partial class TabMainView : UserControl
    {
        public TabMainView()
        {
            InitializeComponent();
            DataContext = this;
        }

        private void DataGridView_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            e.Handled = false;
        }
    }
}