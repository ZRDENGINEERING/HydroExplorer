
namespace HydroExplorer.ViewModel.TabBotItem
{
    class TabBotChartsViewModel : TabBotViewModelBase
    {
        private string _header = "Home";
        public override string Header
        {
            get => _header;
            set { _header = value; OnPropertyChanged(nameof(Header)); }
        }
    }
}
