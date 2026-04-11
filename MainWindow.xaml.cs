using HydroExplorer.Utils;
using HydroExplorer.View;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Input;



namespace HydroExplorer
{
    public partial class MainWindow : Window
    {
        public static ServiceProvider ServiceProvider { get; private set; }

        public MainWindow()
        {
            InitializeComponent();
        }


        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }


        public static async void PlotMaker()
        {
            var plotWindow = new PlotWindow();
            plotWindow.Show();
            await plotWindow.LoadDataAsync();

        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

    }
}

