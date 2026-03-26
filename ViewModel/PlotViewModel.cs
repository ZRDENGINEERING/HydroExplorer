using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System.ComponentModel;
using System.Runtime.CompilerServices;



namespace HydroExplorer.ViewModel
{
    public class PlotViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private PlotModel _plotModel;
        public PlotModel PlotModel
        {
            get => _plotModel;
            set { _plotModel = value; OnPropertyChanged(); }
        }

        public PlotViewModel()
        {
            PlotModel = CreatePlot(GetSampleData());
        }

        private PlotModel CreatePlot(List<(double x, double y)> data)
        {
            var model = new PlotModel { Title = "X-Y Plot" };

            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Title = "X"
            });
            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "Y"
            });

            var series = new LineSeries
            {
                Title = "Data",
                MarkerType = MarkerType.Circle,
                MarkerSize = 4
            };

            foreach (var (x, y) in data)
                series.Points.Add(new DataPoint(x, y));

            model.Series.Add(series);
            return model;
        }

        // Replace this with your real data
        private List<(double x, double y)> GetSampleData()
        {
            var data = new List<(double, double)>();
            for (int i = 0; i <= 20; i++)
                data.Add((i, i * i * 0.5));
            return data;
        }

        // Call this to update the plot with new data
        public void UpdatePlot(List<(double x, double y)> data)
        {
            PlotModel = CreatePlot(data);
        }
    }
}