namespace HydroExplorer.ViewModel.TabItem
{
    public class TabHomeViewModel : TabViewModelBase
    {
        private string _header = "Home";
        public override string Header
        {
            get => _header;
            set { _header = value; OnPropertyChanged(nameof(Header)); }
        }
    }
}