using HydroExplorer.View;
using System.Collections.ObjectModel;

namespace HydroExplorer.ViewModel
{
    class DataGridViewModelHMS
    {
        public ObservableCollection<HecHmsRsltGlobalSummary> HMSResults { get; set; } = [];
    }
}
