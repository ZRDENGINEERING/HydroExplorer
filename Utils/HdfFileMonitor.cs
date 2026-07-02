using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HydroExplorer.Helpers
{
    /// <summary>
    /// Watches HDF/DSS files on disk for changes (e.g. HEC-RAS re-running a plan
    /// while the model is open) and raises a debounced FileChanged event once
    /// writes have settled. Registered as a DI singleton so multiple views
    /// (MapOverView, DataGridView, etc.) can share the same watchers and all
    /// react to the same event.
    /// </summary>
    public class HdfFileMonitor : IDisposable
    {
        private readonly Dictionary<string, FileSystemWatcher> _watchers = [];
        private readonly Dictionary<string, CancellationTokenSource> _debounceCts = [];
        private readonly TimeSpan _debounce = TimeSpan.FromSeconds(3);

        public event Action<string>? FileChanged;

        /// <summary>
        /// Starts watching a file. Safe to call repeatedly for the same path —
        /// if it's already being watched, this is a no-op. Because watchers are
        /// keyed globally by path, callers should NOT assume they "own" the
        /// watch; use Unwatch/UnwatchAll carefully if multiple views share paths.
        /// </summary>
        public void Watch(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            if (_watchers.ContainsKey(path)) return;

            string dir = Path.GetDirectoryName(path)!;
            string file = Path.GetFileName(path);

            var watcher = new FileSystemWatcher(dir, file)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };

            watcher.Changed += (_, e) => OnChanged(e.FullPath);
            watcher.Created += (_, e) => OnChanged(e.FullPath);
            watcher.Renamed += (_, e) => OnChanged(e.FullPath);
            _watchers[path] = watcher;

            System.Diagnostics.Debug.WriteLine($"HdfFileMonitor: watching '{file}'");
        }

        public void Unwatch(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (!_watchers.TryGetValue(path, out var watcher)) return;

            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
            _watchers.Remove(path);

            if (_debounceCts.TryGetValue(path, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
                _debounceCts.Remove(path);
            }
        }

        public void UnwatchAll()
        {
            foreach (var path in _watchers.Keys.ToList())
                Unwatch(path);
        }

        private void OnChanged(string path)
        {
            // Debounce — HEC-RAS writes HDF in multiple passes
            if (_debounceCts.TryGetValue(path, out var existing))
            {
                existing.Cancel();
                existing.Dispose();
            }

            var cts = new CancellationTokenSource();
            _debounceCts[path] = cts;

            Task.Delay(_debounce, cts.Token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;
                System.Diagnostics.Debug.WriteLine($"HdfFileMonitor: change detected '{Path.GetFileName(path)}'");
                FileChanged?.Invoke(path);
            }, TaskScheduler.Default);
        }

        public void Dispose() => UnwatchAll();
    }
}