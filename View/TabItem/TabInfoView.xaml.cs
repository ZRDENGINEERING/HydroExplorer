using HydroExplorer.Helpers;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;

namespace HydroExplorer.View.TabItem
{
    public partial class TabInfoView : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void NotifyPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private static MapOverView? _mapOverView;

        public TabInfoView()
        {
            InitializeComponent();

            if (_mapOverView == null)
                _mapOverView = new MapOverView();

            MapContainer.Content = _mapOverView;

            Loaded += async (s, e) =>
            {
                var settingsRepo = App.ServiceProvider.GetRequiredService<IUserSettingsRepo>();
                var settings = await settingsRepo.GetSettingsFresh();

                // Fall back to most recently opened project if LastProjPath not set
                string projPath = settings.LastProjPath;
                if (string.IsNullOrEmpty(projPath) && settings.Projects.Count > 0)
                {
                    projPath = settings.Projects
                        .OrderByDescending(kvp => kvp.Value.LastOpened)
                        .First().Key;

                    // Persist it so next launch is faster
                    settings.LastProjPath = projPath;
                    settings.ProjPath = projPath;
                    settings.ProjDir = settings.Projects[projPath].ProjDir;
                    await settingsRepo.SaveSettings(settings);
                }

                if (!string.IsNullOrEmpty(projPath))
                {
                    string projName = Path.GetFileNameWithoutExtension(projPath);
                    txtBlockProjectName.Text = $"Project Name: {projName}";
                    txtBlockAreaSqMi.Text = $"Drainage Area (sq.mi.): 22";
                    txtBlockAreaAcre.Text = $"Drainage Area (acre): 222";
                    txtBlockUSGSInfo.Text = $"USGS Info: USGS Info";
                    txtBlockSiteNumber.Text = $"Site Number: Site No";
                    txtBlockStationName.Text = $"Station Name: Station Name";
                    txtBlockHUC.Text = $"HUC Code: HUC";
                }
            };

            EventBus.ProjPathSelected += path =>
            {
                Dispatcher.Invoke(() =>
                {
                    string projectName = Path.GetFileNameWithoutExtension(path);
                    txtBlockProjectName.Text = $"Project Name: {projectName}";
                    txtBlockAreaSqMi.Text = $"Drainage Area (sq.mi.): {projectName}";
                    txtBlockAreaAcre.Text = $"Drainage Area (acre): {projectName}";
                    txtBlockUSGSInfo.Text = $"USGS Info: {projectName}";
                    txtBlockSiteNumber.Text = $"Site Number: {projectName}";
                    txtBlockStationName.Text = $"Station Name: {projectName}";
                    txtBlockHUC.Text = $"HUC Code: {projectName}";
                });
            };
        }
    }
}