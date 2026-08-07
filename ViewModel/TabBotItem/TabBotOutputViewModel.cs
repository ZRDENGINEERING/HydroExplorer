using HydroExplorer.Helpers;
using System.Collections.ObjectModel;


namespace HydroExplorer.ViewModel.TabBotItem
{
    public class TabBotOutputViewModel : TabBotViewModelBase
    {
        private string _header = "Output";
        public override string Header
        {
            get => _header;
            set { _header = value; OnPropertyChanged(nameof(Header)); }
        }

        private const int MaxDisplayedLines = 2000;

        public ObservableCollection<string> Lines { get; } = [];

        private bool _autoScroll = true;
        public bool AutoScroll
        {
            get => _autoScroll;
            set { _autoScroll = value; OnPropertyChanged(); }
        }
        
        private readonly Queue<string> _pending = new();
        private readonly Lock _pendingLock = new();
        private bool _flushScheduled;

        public TabBotOutputViewModel()
        {
            foreach (var line in DebugOutputCapture.Instance.History)
                Lines.Add(line);

            TrimToMax();

            DebugOutputCapture.Instance.LineWritten += OnLineWritten;
        }

        private void OnLineWritten(string line)
        {
            lock (_pendingLock)
            {
                _pending.Enqueue(line);
                if (_flushScheduled) return;
                _flushScheduled = true;
            }

            System.Windows.Application.Current?.Dispatcher.InvokeAsync(FlushPending,
                System.Windows.Threading.DispatcherPriority.Background);
        }

        private void FlushPending()
        {
            string[] batch;
            lock (_pendingLock)
            {
                batch = [.. _pending];
                _pending.Clear();
                _flushScheduled = false;
            }

            foreach (var line in batch)
                Lines.Add(line);

            TrimToMax();
        }

        private void TrimToMax()
        {
            while (Lines.Count > MaxDisplayedLines)
                Lines.RemoveAt(0);
        }
    }
}