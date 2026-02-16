using System.ComponentModel;
using System.Runtime.CompilerServices;


namespace HydroExplorer.Core
{
    public class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChangedEventHandler? handler = PropertyChanged;

            if (handler != null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }

        // Implement INotifyPropertyChanged interface details here...
    }
}