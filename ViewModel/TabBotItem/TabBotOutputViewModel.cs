using HydroExplorer.Helpers;
using System.Collections.ObjectModel;
using System.Collections.Specialized;



namespace HydroExplorer.ViewModel.TabBotItem
{
    public class TabBotOutputViewModel : TabBotViewModelBase
    {
        private string _header = "Output Window";
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

        // Pending lines queued from any thread, flushed to Lines on the UI thread.
        // Using a queue + single pending-dispatch flag avoids re-entrant
        // CollectionChanged events that WPF's ItemsControl can't tolerate — if
        // Dispatcher.InvokeAsync itself triggers a Debug.WriteLine (entirely
        // plausible given how much diagnostic logging this app has), a naive
        // per-line InvokeAsync would nest collection mutations mid-bind.
        private readonly Queue<string> _pending = new();
        private readonly Lock _pendingLock = new();
        private bool _flushScheduled;

        public TabBotOutputViewModel()
        {
            // Backfill history synchronously — constructor runs on the UI thread,
            // so direct collection mutation is safe here.
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

            // Schedule one flush per "burst" — subsequent lines queued before
            // this dispatch runs will be picked up in the same flush, keeping
            // CollectionChanged notifications batched and WPF's ItemsControl happy.
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