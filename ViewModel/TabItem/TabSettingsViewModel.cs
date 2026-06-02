using HydroExplorer.Core;
using HydroExplorer.Helpers;
using System.Text;
using System.Windows.Input;

namespace HydroExplorer.ViewModel.TabItem
{
    public class TabSettingsViewModel : TabViewModelBase
    {
        public static TabSettingsViewModel? Instance { get; private set; }

        private string _header = "Output";
        public override string Header
        {
            get => _header;
            set { _header = value; OnPropertyChanged(nameof(Header)); }
        }

        private readonly StringBuilder _buffer = new();

        private string _outputText = string.Empty;
        public string OutputText
        {
            get => _outputText;
            private set
            {
                _outputText = value;
                OnPropertyChanged(nameof(OutputText));
                OnPropertyChanged(nameof(LineCount));
            }
        }

        private string _statusText = "Ready";
        public string StatusText
        {
            get => _statusText;
            private set { _statusText = value; OnPropertyChanged(nameof(StatusText)); }
        }

        private bool _autoScroll = true;
        public bool AutoScroll
        {
            get => _autoScroll;
            set { _autoScroll = value; OnPropertyChanged(nameof(AutoScroll)); }
        }

        public int LineCount => _outputText.Split('\n').Length;

        public ICommand ClearCommand { get; }

        public TabSettingsViewModel()
        {
            Instance = this;

            ClearCommand = new RelayCommand(_ => Clear());

            EventBus.ProjPathChanged += path =>
            {
                AppendLine($"[{DateTime.Now:HH:mm:ss}] Project loaded: {System.IO.Path.GetFileNameWithoutExtension(path)}");
            };



            EventBus.HmsPathChanged += path =>
            {
                AppendLine($"[{DateTime.Now:HH:mm:ss}] HMS path set: {System.IO.Path.GetFileName(path)}");
            };

            EventBus.HdfPathASelected += path =>
            {
                AppendLine($"[{DateTime.Now:HH:mm:ss}] HDF A: {System.IO.Path.GetFileName(path)}");
            };

            EventBus.HdfPathBSelected += path =>
            {
                AppendLine($"[{DateTime.Now:HH:mm:ss}] HDF B: {System.IO.Path.GetFileName(path)}");
            };
        }

        public void AppendLine(string message)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                _buffer.AppendLine(message);
                OutputText = _buffer.ToString();
                StatusText = message.Length > 80 ? message[..80] + "..." : message;
            });
        }

        public void Append(string message)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                _buffer.Append(message);
                OutputText = _buffer.ToString();
            });
        }

        public void Clear()
        {
            _buffer.Clear();
            OutputText = string.Empty;
            StatusText = "Cleared";
        }
    }
}