namespace HydroExplorer.ViewModel.TabItem
{
    public class TabPublishViewModel : TabViewModelBase
    {
        private string _header = "Home";
        public override string Header
        {
            get => _header;
            set { _header = value; OnPropertyChanged(nameof(Header)); }
        }


    }
}