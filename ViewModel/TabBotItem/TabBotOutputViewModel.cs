namespace HydroExplorer.ViewModel.TabBotItem
{
    public class TabBotOutputViewModel : TabBotViewModelBase
    {
        private string _header = "TabBotOutputViewModel";
        public override string Header
        {
            get => _header;
            set { _header = value; OnPropertyChanged(nameof(Header)); }
        }
    }
}