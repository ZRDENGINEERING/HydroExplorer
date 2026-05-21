using HydroExplorer.Utils;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;



namespace HydroExplorer.View
{
    public partial class DataGridViewHMS : UserControl
    {
        private ObservableCollection<DssRecord> _allRecords = new();
        private ICollectionView _view;

        // Dependency property so parent windows can set the DSS file path
        public static readonly DependencyProperty DssFilePathProperty =
            DependencyProperty.Register(nameof(DssFilePath), typeof(string),
                typeof(DataGridViewHMS),
                new PropertyMetadata(null, OnDssFilePathChanged));

        public string DssFilePath
        {
            get => (string)GetValue(DssFilePathProperty);
            set => SetValue(DssFilePathProperty, value);
        }

        private static void OnDssFilePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DataGridViewHMS ctrl && e.NewValue is string path)
                ctrl.LoadAsync(path);
        }

        public DataGridViewHMS() => InitializeComponent();



        public async Task LoadAsync(string dssPath)
        {
            StatusText.Text = "Loading…";
            var sw = Stopwatch.StartNew();

            _allRecords = await Task.Run(() => DssDataService.Load(dssPath));

            var parameters = _allRecords
                .Select(r => r.PartC)
                .Distinct()
                .OrderBy(x => x)
                .Prepend("All")
                .ToList();

            PartCCombo.ItemsSource = parameters;
            PartCCombo.SelectedIndex = 0;

            _view = CollectionViewSource.GetDefaultView(_allRecords);
            _view.Filter = ApplyFilter;
            DssGrid.ItemsSource = _view;

            sw.Stop();
            UpdateStatus(sw.Elapsed);
        }



        // ── Filtering ────────────────────────────────────────────────
        private bool ApplyFilter(object obj)
        {
            if (obj is not DssRecord r) return false;

            var keyword = FilterBox.Text.Trim();
            var partC = PartCCombo.SelectedItem as string;

            bool textMatch = string.IsNullOrEmpty(keyword) ||
                r.Pathname.Contains(keyword, StringComparison.OrdinalIgnoreCase);

            bool partCMatch = string.IsNullOrEmpty(partC) ||
                partC == "All" || r.PartC == partC;

            return textMatch && partCMatch;
        }

        private void FilterBox_TextChanged(object s, TextChangedEventArgs e)
        {
            _view?.Refresh();
            UpdateStatus(null);
        }

        private void PartCCombo_SelectionChanged(object s, SelectionChangedEventArgs e)
        {
            _view?.Refresh();
            UpdateStatus(null);
        }

        private void ClearFilter_Click(object s, RoutedEventArgs e)
        {
            FilterBox.Clear();
            PartCCombo.SelectedIndex = 0;
            _view?.Refresh();
        }

        private void ExportCsv_Click(object s, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog { Filter = "CSV files|*.csv", FileName = "hms_export.csv" };
            if (dlg.ShowDialog() != true) return;

            var visible = _view.Cast<DssRecord>().ToList();
            using var w = new StreamWriter(dlg.FileName);
            w.WriteLine("PartA,PartB,PartC,PartD,PartE,PartF,Units,Type,Count,Pathname");
            foreach (var r in visible)
                w.WriteLine($"{r.PartA},{r.PartB},{r.PartC},{r.PartD},{r.PartE}," +
                            $"{r.PartF},{r.Units},{r.Type},{r.Count},{r.Pathname}");

            StatusText.Text = $"Exported {visible.Count} rows → {dlg.FileName}";
        }

        private void CopyClipboard_Click(object s, RoutedEventArgs e)
        {
            var sb = new StringBuilder();
            sb.AppendLine("PartA\tPartB\tPartC\tPartD\tPartE\tPartF\tUnits\tType\tCount");
            foreach (var r in _view.Cast<DssRecord>())
                sb.AppendLine($"{r.PartA}\t{r.PartB}\t{r.PartC}\t{r.PartD}\t{r.PartE}\t" +
                              $"{r.PartF}\t{r.Units}\t{r.Type}\t{r.Count}");
            Clipboard.SetText(sb.ToString());
            StatusText.Text = "Copied to clipboard.";
        }

        // ── Helpers ──────────────────────────────────────────────────
        private void UpdateStatus(TimeSpan? elapsed)
        {
            int showing = _view?.Cast<object>().Count() ?? 0;
            RowCountText.Text = $"{showing:N0} of {_allRecords.Count:N0} records";
            if (elapsed.HasValue)
                StatusText.Text = $"Loaded in {elapsed.Value.TotalSeconds:F2}s";
        }


        private void DssGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DssGrid.SelectedItem is DssRecord record)
            {
                var ts = DssDataService.LoadValues(record, DssFilePath);
                if (ts == null || ts.Count == 0) return;

                // ts.Times  → DateTime[]
                // ts.Values → double[]
                // Pass to your chart/detail view here
            }
        }
    }



    public class HecHmsRsltGlobalSummary : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    }


    public class HecHmsRsltSummary : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public string River { get; set; }
        public string Reach { get; set; }
        public string RiverSta { get; set; }
        public string Profile { get; set; }
        public float QTotal { get; set; }
        public float MinChEl { get; set; }
        public double WSElev { get; set; }
        public double MaxDepths { get; set; }
        public double CritWS { get; set; }
        public double EGElev { get; set; }
        public double EGSlope { get; set; }
        public double VelChnl { get; set; }
        public double FlowArea { get; set; }
        public double TopWidth { get; set; }
        public double FrChnl { get; set; }
    }





}
