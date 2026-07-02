using System.Diagnostics;
using System.Text;



namespace HydroExplorer.Helpers
{
    public sealed class DebugOutputCapture : TraceListener
    {
        public static DebugOutputCapture Instance { get; } = new();

        public event Action<string>? LineWritten;

        private const int MaxHistory = 2000;
        private readonly Lock _historyLock = new();
        private readonly LinkedList<string> _history = new();

        public IReadOnlyList<string> History
        {
            get
            {
                lock (_historyLock)
                    return [.. _history];
            }
        }

        private readonly StringBuilder _pendingLine = new();

        private DebugOutputCapture() { }

        public override void Write(string? message)
        {
            if (string.IsNullOrEmpty(message)) return;
            _pendingLine.Append(message);
        }

        public override void WriteLine(string? message)
        {
            _pendingLine.Append(message ?? string.Empty);
            string line = _pendingLine.ToString();
            _pendingLine.Clear();

            Publish(line);
        }

        private void Publish(string line)
        {
            lock (_historyLock)
            {
                _history.AddLast(line);
                while (_history.Count > MaxHistory)
                    _history.RemoveFirst();
            }

            try
            {
                LineWritten?.Invoke(line);
            }
            catch
            {
            }
        }
    }
}
