using System.ComponentModel;

public class TabItemViewModel : INotifyPropertyChanged
{
    public string Header { get; set; }

    private object? _content;
    public object? Content
    {
        get => _content;
        set { _content = value; OnPropertyChanged(nameof(Content)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}