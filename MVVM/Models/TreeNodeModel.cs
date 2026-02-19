using HydroExplorer.MVVM.ViewModels;
using System.Collections.ObjectModel;


namespace HydroExplorer.MVVM.Models
{
    public class TreeNodeModel : BaseViewModel
    {
        public string Header { get; set; }
        public string Tag { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public ObservableCollection<TreeNodeModel> Children { get; set; } = new();

        //public ObservableCollection<TreeNodeModel> Children { get; set; } = new();

    }
}