using HydroExplorer.Helpers;
using System.Windows;
using System.Windows.Input;


namespace HydroExplorer
{
    public partial class MainWindow : Window
    {
        private string? _lastTopTabHeader = null;
        private bool _hasAutoSwitchedToHydrology = false;

        public MainWindow()
        {
            InitializeComponent();
            
            LeftTabControl.SelectedIndex = 0;
            bool _firstTopTabEvent = true;

            EventBus.TopTabChanged += topTabHeader =>
            {
                if (_firstTopTabEvent)
                {
                    _firstTopTabEvent = false;
                    System.Diagnostics.Debug.WriteLine("MainWindow: ignoring first event");
                    return;
                }

                bool isChartsTab = topTabHeader == "HMS Charts" || topTabHeader == "Info";
                bool isRealChange = topTabHeader != _lastTopTabHeader;
                _lastTopTabHeader = topTabHeader;

                if (isChartsTab && isRealChange && !_hasAutoSwitchedToHydrology && _startupComplete)
                {
                    _hasAutoSwitchedToHydrology = true;
                    Dispatcher.Invoke(() => LeftTabControl.SelectedIndex = 1);
                }

                _startupComplete = true;
            };
        }

        private bool _startupComplete = false;


        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
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