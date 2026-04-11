using System.ComponentModel;


namespace HydroExplorer.ViewModel.TabBotItem
{
    public abstract class TabBotViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public abstract string Header { get; set; }
        public object Content { get; set; }
    }
}