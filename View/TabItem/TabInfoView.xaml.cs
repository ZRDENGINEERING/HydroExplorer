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


        public TabInfoView()
        {
            InitializeComponent();

            Loaded += async (s, e) =>
            {
                var settingsRepo = App.ServiceProvider.GetRequiredService<IUserSettingsRepo>();
                var settings = await settingsRepo.GetSettings();

                if (!string.IsNullOrEmpty(settings.LastProjPath))
                {
                    string projName = Path.GetFileNameWithoutExtension(settings.LastProjPath);

                    string projAreaSqMi = "22";
                    string projAreaSqAcre = "222";
                    string projUSGSInfo = "USGS Info";
                    string projSiteNumber = "Site No";
                    string projStationName = "Station Name";
                    string projBlockHUC = "HUC";


                    txtBlockProjectName.Text = $"Project Name: {projName}";
                    txtBlockAreaSqMi.Text = $"Drainage Area (sq.mi.): {projAreaSqMi}";
                    txtBlockAreaAcre.Text = $"Drainage Area (acre): {projAreaSqAcre}";
                    txtBlockUSGSInfo.Text = $"USGS Info: {projUSGSInfo}";
                    txtBlockSiteNumber.Text = $"Site Number: {projSiteNumber}";
                    txtBlockStationName.Text = $"Station Name: {projStationName}";
                    txtBlockHUC.Text = $"HUC Code: {projBlockHUC}";

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





        private async Task OnProjPathChanged(string projPath)
        {
            if (string.IsNullOrEmpty(projPath)) return;

            // ✅ Update project name from .prj file or folder name
            string projectName = Path.GetFileNameWithoutExtension(projPath);
            txtBlockProjectName.Text = $"Project Name: {projectName}";
            txtBlockAreaSqMi.Text = $"Drainage Area (sq.mi.): {projectName}";
            txtBlockAreaAcre.Text = $"Drainage Area (acre): {projectName}";
            txtBlockUSGSInfo.Text = $"USGS Info: {projectName}";
            txtBlockSiteNumber.Text = $"Site Number: {projectName}";
            txtBlockStationName.Text = $"Station Name: {projectName}";
            txtBlockHUC.Text = $"HUC Code: {projectName}";
        }




    }
}
