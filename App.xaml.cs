using HydroExplorer.Helpers;
using HydroExplorer.ViewModel;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;


namespace HydroExplorer
{
    public partial class App : Application
    {
        public static ServiceProvider ServiceProvider { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var services = new ServiceCollection();

            services.AddSingleton<IUserSettingsRepo, FileSystemUserSettingsRepo>();
        
            services.AddSingleton<MainWindowViewModel>();

            services.AddSingleton<PlotViewModel>();

            services.AddSingleton<SelectionViewModel>();


            ServiceProvider = services.BuildServiceProvider();

            var mainWindow = new MainWindow
            {
                DataContext = ServiceProvider.GetRequiredService<MainWindowViewModel>()
            };
            mainWindow.Show();


            var plotVm = ServiceProvider.GetRequiredService<PlotViewModel>();
            _ = Task.Run(async () => await plotVm.LoadDataAsync());

        }




    protected override void OnExit(ExitEventArgs e)
    {
        ServiceProvider?.Dispose();
        base.OnExit(e);
    }

    }
}