using System.IO;
using System.Text.Json;



namespace HydroExplorer.Helpers
{
    public class UserSettings
    {
        public string LastProjPath { get; set; } = string.Empty;
        public string ProjPath { get; set; } = string.Empty;
        public string ProjDir { get; set; } = string.Empty;
        public int NextOpenOrder { get; set; } = 0;


        public Dictionary<string, ProjectSettings> Projects { get; set; } = [];
        public Dictionary<string, DateTime> HmsProjects { get; set; } = [];

        public IEnumerable<KeyValuePair<string, ProjectSettings>> RecentProjects =>
            Projects
                .Reverse()
                .Take(10);

        public IEnumerable<KeyValuePair<string, DateTime>> RecentHmsProjects =>
            HmsProjects
                .Reverse()
                .Take(10);

        public override string ToString() => $"{{ ProjPath: \"{ProjPath}\" }}";
    }


    public class ProjectSettings
    {
        public string ProjDir { get; set; } = string.Empty;
        public string HdfPathA { get; set; } = string.Empty;
        public string HdfPathB { get; set; } = string.Empty;
        public string PlanNameA { get; set; } = string.Empty;
        public string PlanNameB { get; set; } = string.Empty;
        public string ProName { get; set; } = string.Empty;
        public string HmsPath { get; set; } = string.Empty;
        public string SelectedReach { get; set; } = string.Empty;
        public DateTime LastOpened { get; set; } = DateTime.MinValue;
        public int OpenOrder { get; set; } = 0; // ADD THIS
    }

    public interface IUserSettingsRepo
    {
        Task<UserSettings> GetSettings();
        Task<UserSettings> GetSettingsFresh();
        Task SaveSettings(UserSettings userSettings);
    }

    public class RecentProjectEntry
    {
        public string Name { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public DateTime LastOpened { get; set; }
        public int OpenOrder { get; set; }
    }


    public sealed class FileSystemUserSettingsRepo : IUserSettingsRepo
    {
        private const string AppName = "HydroExplorer";
        private const string SettingsFileName = "userSettings.json";

        private readonly string _settingsFolderPath = GetSettingsFolderPath();
        private readonly string _settingsFilePath = GetSettingsFilePath();

        private readonly Lock _lock = new();
        private UserSettings? _cache;

        public Task<UserSettings> GetSettings()
        {
            lock (_lock)
            {
                if (_cache != null) return Task.FromResult(_cache);

                if (!File.Exists(_settingsFilePath))
                {
                    _cache = new UserSettings();
                    SaveSettingsInternal(_cache);
                    return Task.FromResult(_cache);
                }

                using var stream = new FileStream(
                    _settingsFilePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);

                _cache = JsonSerializer.Deserialize<UserSettings>(stream)
                    ?? throw new InvalidOperationException("Can't deserialize user settings");

                return Task.FromResult(_cache);
            }
        }

        public Task<UserSettings> GetSettingsFresh()
        {
            lock (_lock) { _cache = null; }
            return GetSettings();
        }

        public Task SaveSettings(UserSettings userSettings)
        {
            lock (_lock)
            {
                _cache = userSettings;
                SaveSettingsInternal(userSettings);
            }
            return Task.CompletedTask;
        }

        private void SaveSettingsInternal(UserSettings userSettings)
        {
            if (!Directory.Exists(_settingsFolderPath))
                Directory.CreateDirectory(_settingsFolderPath);

            const int maxRetries = 3;
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    using var stream = new FileStream(
                        _settingsFilePath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None);

                    JsonSerializer.Serialize(stream, userSettings);
                    return;
                }
                catch (IOException) when (i < maxRetries - 1)
                {
                    Thread.Sleep(100);
                }
            }
        }

        private static string GetSettingsFolderPath()
        {
            var appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(appDataFolder, AppName);
        }

        private static string GetSettingsFilePath()
        {
            var settingsFolder = GetSettingsFolderPath();
            return Path.Combine(settingsFolder, SettingsFileName);
        }

    }
}