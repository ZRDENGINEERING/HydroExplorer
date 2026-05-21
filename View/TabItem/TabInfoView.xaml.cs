using HydroExplorer.Helpers;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;



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
                var settings = await settingsRepo.GetSettings();

                if (!string.IsNullOrEmpty(settings.LastProjPath))
                {
                    string projName = Path.GetFileNameWithoutExtension(settings.LastProjPath);

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
