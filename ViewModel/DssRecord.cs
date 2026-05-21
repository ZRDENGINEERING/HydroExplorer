using System.ComponentModel;
using System.Runtime.CompilerServices;

public class DssRecord : INotifyPropertyChanged
{
    public string PartA { get; set; }   // Basin
    public string PartB { get; set; }   // Location
    public string PartC { get; set; }   // Parameter (FLOW, PRECIP…)
    public string PartD { get; set; }   // Start date
    public string PartE { get; set; }   // Interval (1Hour, 1Day…)
    public string PartF { get; set; }   // Version / run label
    public string Pathname => $"/{PartA}/{PartB}/{PartC}/{PartD}/{PartE}/{PartF}/";
    public string Units { get; set; }
    public string Type { get; set; }
    public int Count { get; set; }   // number of values in the time series
    public double[] Values { get; set; }   // lazy-loaded on demand

    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}