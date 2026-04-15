using HydroExplorer.Helpers;
using HydroExplorer.ViewModel;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Windows;



namespace HydroExplorer
{
    public partial class App : Application
    {
        public static ServiceProvider ServiceProvider { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            CleanupTempShapefiles();

            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                System.Diagnostics.Debug.WriteLine($"Unobserved task exception: {args.Exception}");
                args.SetObserved();
            };

            DispatcherUnhandledException += (s, args) =>
            {
                System.Diagnostics.Debug.WriteLine($"Dispatcher exception: {args.Exception}");
                args.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                System.Diagnostics.Debug.WriteLine($"Unhandled exception: {args.ExceptionObject}");
            };

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

            // Fire and forget safely on UI thread - no Task.Run
            var plotVm = ServiceProvider.GetRequiredService<PlotViewModel>();
            _ = plotVm.LoadDataAsync();
        }




        protected override void OnExit(ExitEventArgs e)
        {
            ServiceProvider?.Dispose();
            base.OnExit(e);
        }





        private static void CleanupTempShapefiles()
        {
            try
            {
                string tempDir = @"C:\Temp";
                if (!Directory.Exists(tempDir)) return;

                //string[] extensions = ["*.shp", "*.shx", "*.dbf", "*.prj", "*.cpg"];
                string[] extensions = { ".shp", ".shx", ".dbf", ".prj", ".cpg", ".sbn", ".sbx" };


                int deleted = 0;

                foreach (var ext in extensions)
                {
                    foreach (var file in Directory.GetFiles(tempDir, $"tmp_*{ext}"))
                    {
                        try
                        {

                            File.Delete(file);
                            deleted++;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            System.Diagnostics.Debug.WriteLine($"CleanupTempShapefiles: skipping {file} — {ex.Message}");
                        }
                    }
                }

                if (deleted > 0)
                    System.Diagnostics.Debug.WriteLine($"CleanupTempShapefiles: deleted {deleted} orphaned temp files.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CleanupTempShapefiles error: {ex.Message}");
            }
        }





    }

}