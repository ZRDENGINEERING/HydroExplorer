using HydroExplorer.Utils;
using HydroExplorer.View;
using System.Collections.ObjectModel;

namespace HydroExplorer.ViewModel
{
    public class DataGridViewModel : BaseViewModel
    {
        public ObservableCollection<HecRasProfileResult> ProfileResults { get; set; } = [];
    }
}