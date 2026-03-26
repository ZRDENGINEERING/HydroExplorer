using System.ComponentModel;


namespace HydroExplorer.ViewModel.TabItem
{
    public abstract class TabViewModelBase : INotifyPropertyChanged
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