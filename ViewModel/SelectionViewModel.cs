using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HydroExplorer.ViewModel
{
    public class SelectionViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private string? _selectedRiverSta;
        public string? SelectedRiverSta
        {
            get => _selectedRiverSta;
            set
            {
                _selectedRiverSta = value;
                OnPropertyChanged();
            }
        }

        private double _selectedLat;
        public double SelectedLat
        {
            get => _selectedLat;
            set { _selectedLat = value; OnPropertyChanged(); }
        }

        private double _selectedLon;
        public double SelectedLon
        {
            get => _selectedLon;
            set { _selectedLon = value; OnPropertyChanged(); }
        }


        private double _selectedDelta;
        public double SelectedDelta
        {
            get => _selectedDelta;
            set { _selectedDelta = value; OnPropertyChanged(); }
        }








    }
}