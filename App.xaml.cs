using HydroExplorer.Helpers;
using HydroExplorer.ViewModel;
using HydroExplorer.ViewModel.TabItem;
using Mapsui.Widgets.InfoWidgets;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
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

            Trace.Listeners.Add(DebugOutputCapture.Instance);

            System.Diagnostics.PresentationTraceSources.DataBindingSource.Switch.Level =
            System.Diagnostics.SourceLevels.Critical;

            LoggingWidget.ShowLoggingInMap = Mapsui.Widgets.ActiveMode.No;
            Mapsui.Logging.Logger.LogDelegate = null;


            CleanupTempShapefiles();
            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                var ex = args.Exception?.InnerException ?? args.Exception;

                if (ex is System.Net.Sockets.SocketException ||
                    ex is System.IO.IOException { InnerException: System.Net.Sockets.SocketException } ||
                    ex is OperationCanceledException)
                {
                    args.SetObserved();
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"Unobserved task exception: {args.Exception}");
                args.SetObserved();
            };

            CleanupTempShapefiles();
            _ = CleanupStaleProjectsAsync();

            DispatcherUnhandledException += (s, args) =>
            {
                if (args.Exception is System.Net.Sockets.SocketException ||
                    args.Exception?.InnerException is System.Net.Sockets.SocketException ||
                    args.Exception is OperationCanceledException ||
                    args.Exception?.InnerException is OperationCanceledException)
                {
                    args.Handled = true;
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"Dispatcher exception: {args.Exception}");
                args.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                System.Diagnostics.Debug.WriteLine($"Unhandled: {ex?.GetType().Name}");
                System.Diagnostics.Debug.WriteLine($"Message: {ex?.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack: {ex?.StackTrace}");
            };



            var services = new ServiceCollection();
            services.AddSingleton<IUserSettingsRepo, FileSystemUserSettingsRepo>();
            services.AddSingleton<HdfFileMonitor>();
            services.AddSingleton<TabInfoViewModel>();
            services.AddSingleton<TabControlViewModel>();
            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton<MapStateService>();
            services.AddSingleton<PlotViewModel>();
            services.AddSingleton<SelectionViewModel>();

            ServiceProvider = services.BuildServiceProvider();

            var mainWindow = new MainWindow
            {
                DataContext = ServiceProvider.GetRequiredService<MainWindowViewModel>()
            };
            mainWindow.Show();
        }





        private static void CleanupTempShapefiles()
        {
            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "HydroExplorer");
                if (!Directory.Exists(tempDir)) return;

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
                            //System.Diagnostics.Debug.WriteLine($"CleanupTempShapefiles: skipping {file} — {ex.Message}");
                        }
                    }
                }

                //if (deleted > 0)
                    //System.Diagnostics.Debug.WriteLine($"CleanupTempShapefiles: deleted {deleted} orphaned temp files.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CleanupTempShapefiles error: {ex.Message}");
            }
        }


        private static async Task CleanupStaleProjectsAsync()
        {
            try
            {
                var settingsRepo = ServiceProvider?.GetService<IUserSettingsRepo>();
                if (settingsRepo == null) return;

                var settings = await settingsRepo.GetSettings();

                var stalePaths = settings.Projects.Keys
                    .Where(path =>
                    {
                        string dir = Path.GetDirectoryName(path) ?? string.Empty;
                        return !File.Exists(path) && !Directory.Exists(dir);
                    })
                    .ToList();

                if (stalePaths.Count == 0) return;

                foreach (var path in stalePaths)
                {
                    settings.Projects.Remove(path);
                    System.Diagnostics.Debug.WriteLine($"CleanupStaleProjects: removed '{path}'");
                }

                // Also clear LastProjPath if it's stale
                if (!string.IsNullOrEmpty(settings.LastProjPath) &&
                    !File.Exists(settings.LastProjPath))
                {
                    System.Diagnostics.Debug.WriteLine($"CleanupStaleProjects: clearing stale LastProjPath '{settings.LastProjPath}'");
                    settings.LastProjPath = string.Empty;
                    settings.ProjPath = string.Empty;
                }

                await settingsRepo.SaveSettings(settings);
                System.Diagnostics.Debug.WriteLine($"CleanupStaleProjects: removed {stalePaths.Count} stale entries.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CleanupStaleProjects error: {ex.Message}");
            }
        }
    }
}