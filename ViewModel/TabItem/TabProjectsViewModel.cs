namespace HydroExplorer.ViewModel.TabItem
{
    public class TabProjectsViewModel : TabViewModelBase
    {
        //public string Header { get; internal set; }
        //public object Content { get; set; }

        private string _header = "Home";
        public override string Header
        {
            get => _header;
            set { _header = value; OnPropertyChanged(nameof(Header)); }
        }


    }
}