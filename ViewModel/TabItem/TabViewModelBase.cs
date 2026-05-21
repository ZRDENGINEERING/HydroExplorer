using HydroExplorer.Helpers;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;



namespace HydroExplorer.ViewModel.TabItem
{
    public abstract class TabViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;


        protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));


        public abstract string Header { get; set; }
        public object Content { get; set; }


        protected IUserSettingsRepo? SettingsRepo { get; set; }

        private ObservableCollection<RecentProjectEntry> _recentProjects = [];
        public ObservableCollection<RecentProjectEntry> RecentProjects
        {
            get => _recentProjects;
            protected set { _recentProjects = value; OnPropertyChanged(nameof(RecentProjects)); }
        }


        private ObservableCollection<RecentProjectEntry> _recentHmsProjects = [];
        public ObservableCollection<RecentProjectEntry> RecentHmsProjects
        {
            get => _recentHmsProjects;
            protected set { _recentHmsProjects = value; OnPropertyChanged(nameof(RecentHmsProjects)); }
        }


        protected async Task LoadRecentProjectsAsync()
        {
            if (SettingsRepo == null) return;

            var settings = await SettingsRepo.GetSettingsFresh();

            var recent = settings.RecentProjects
                .GroupBy(kvp => kvp.Value.ProjDir, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(kvp => kvp.Value.LastOpened).First())
                .Take(10)
                .Select(kvp => new RecentProjectEntry
                {
                    Name = Path.GetFileNameWithoutExtension(kvp.Key),
                    FilePath = kvp.Value.ProjDir,
                    LastOpened = kvp.Value.LastOpened
                });

            var recentHms = settings.RecentHmsProjects
                .GroupBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(kvp => kvp.Value).First())
                .Take(10)
                .Select(kvp => new RecentProjectEntry
                {
                    Name = Path.GetFileNameWithoutExtension(kvp.Key),
                    FilePath = Path.GetDirectoryName(kvp.Key) ?? string.Empty,
                    LastOpened = kvp.Value
                });

            Application.Current.Dispatcher.Invoke(() =>
            {
                RecentProjects = new ObservableCollection<RecentProjectEntry>(recent);
                RecentHmsProjects = new ObservableCollection<RecentProjectEntry>(recentHms);
            });




        }
    }
}