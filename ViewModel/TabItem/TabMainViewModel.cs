namespace HydroExplorer.ViewModel.TabItem
{
    internal class TabMainViewModel : TabViewModelBase
    {
        private string _header = "Main";
        public override string Header
        {
            get => _header;
            set { _header = value; OnPropertyChanged(nameof(Header)); }
        }
    }
}