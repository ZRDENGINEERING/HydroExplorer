using HydroExplorer.Core;
using HydroExplorer.View;
using HydroExplorer.Helpers;
using System.IO;
using System.Windows.Input;



namespace HydroExplorer.ViewModel.TabItem
{
    public class TabInfoViewModel : TabViewModelBase
    {
        private string _header = "Home";
        public override string Header
        {
            get => _header;
            set { _header = value; OnPropertyChanged(nameof(Header)); }
        }

        private RecentProjectEntry? _selectedRecentProject;
        public RecentProjectEntry? SelectedRecentProject
        {
            get => _selectedRecentProject;
            set
            {
                if (_selectedRecentProject == value) return;
                _selectedRecentProject = value;
                OnPropertyChanged(nameof(SelectedRecentProject));

                if (value != null)
                {
                    OpenRecentProject(value);
                    _selectedRecentProject = null;
                    OnPropertyChanged(nameof(SelectedRecentProject));
                }
            }
        }

        public ICommand ClearAllRecentCommand { get; }

        public TabInfoViewModel(IUserSettingsRepo settingsRepo)
        {
            SettingsRepo = settingsRepo;
            //_ = LoadRecentProjectsAsync();
            _ = ValidateAndLoadRecentProjectsAsync(); // replaces initial LoadRecentProjectsAsync


            EventBus.ProjPathChanged += async _ => await LoadRecentProjectsAsync();
            EventBus.ProjPathSelected += async _ => await LoadRecentProjectsAsync();

            ClearAllRecentCommand = new RelayCommand(async () => await ClearRecentProjectsAsync());
        }

        private async Task ValidateAndLoadRecentProjectsAsync()
        {
            var settings = await SettingsRepo!.GetSettings();
            bool dirty = false;

            // Remove Projects entries whose file no longer exists
            var deadProjects = settings.Projects.Keys
                .Where(k => !File.Exists(k))
                .ToList();

            foreach (var key in deadProjects)
            {
                settings.Projects.Remove(key);
                dirty = true;
            }

            // Remove HmsProjects entries whose file no longer exists
            var deadHms = settings.HmsProjects.Keys
                .Where(k => !File.Exists(k))
                .ToList();

            foreach (var key in deadHms)
            {
                settings.HmsProjects.Remove(key);
                dirty = true;
            }

            if (dirty)
                await SettingsRepo.SaveSettings(settings);

            await LoadRecentProjectsAsync();
        }



        private async void OpenRecentProject(RecentProjectEntry project)
        {
            if (!Directory.Exists(project.FilePath))
            {
                await RemoveRecentProjectAsync(project.FilePath);
                return;
            }


            var projFile = Directory.GetFiles(project.FilePath, "*.rasmap").FirstOrDefault()
                ?? Directory.GetFiles(project.FilePath, "*.prj").FirstOrDefault();

            

            if (projFile == null)
            {
                System.Diagnostics.Debug.WriteLine($"\n OpenRecentProject: projFile NOT FOUND '{projFile}'\n");
                return;
            }

            var settings = await SettingsRepo!.GetSettings();
            if (settings.Projects.TryGetValue(projFile, out var projSettings))
            {
                projSettings.LastOpened = DateTime.Now;
                await SettingsRepo.SaveSettings(settings);
            }

            EventBus.PublishProjPath(projFile);
            EventBus.PublishProjPathChanged(projFile);

            await LoadRecentProjectsAsync();
        }


        private async Task ClearRecentProjectsAsync()
        {
            var settings = await SettingsRepo!.GetSettings();
            settings.HmsProjects.Clear();
            settings.Projects.Clear();
            settings.LastProjPath = string.Empty;
            settings.ProjPath = string.Empty;
            settings.ProjDir = string.Empty;
            await SettingsRepo.SaveSettings(settings);
            await LoadRecentProjectsAsync();
        }


        private async Task RemoveRecentProjectAsync(string dirPath)
        {
            var settings = await SettingsRepo!.GetSettings();

            // Projects keys are full file paths — remove any whose directory matches
            var projKeysToRemove = settings.Projects.Keys
                .Where(k => Path.GetDirectoryName(k)
                    ?.Equals(dirPath, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            foreach (var key in projKeysToRemove)
                settings.Projects.Remove(key);

            // HmsProjects keys are also file paths
            var hmsKeysToRemove = settings.HmsProjects.Keys
                .Where(k => Path.GetDirectoryName(k)
                    ?.Equals(dirPath, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            foreach (var key in hmsKeysToRemove)
                settings.HmsProjects.Remove(key);

            await SettingsRepo.SaveSettings(settings);
            await LoadRecentProjectsAsync();
        }


    }
}
