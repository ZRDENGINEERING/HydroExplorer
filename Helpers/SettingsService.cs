using System.IO;
using System.Text.Json;



namespace HydroExplorer.Helpers
{
    public class UserSettings
    {
        public string LastProjPath { get; set; } = string.Empty;
        public string ProjPath { get; set; } = string.Empty;
        public string ProjDir { get; set; } = string.Empty;
        //public string HdfPathA { get; set; } = string.Empty;
        //public string HdfPathB { get; set; } = string.Empty;
        //public string PlanNameA { get; set; } = string.Empty;
        //public string PlanNameB { get; set; } = string.Empty;
        //public string ProName { get; set; } = string.Empty;

        public Dictionary<string, ProjectSettings> Projects { get; set; } = new();

        public override string ToString() => $"{{ ProjPath: \"{ProjPath}\" }}";
        //public override string ToString() => $"{{ HdfPath: {HdfPath}, ProjPath: \"{ProjPath}\" }}";
        //public override string ToString() => $"{{ HdfPath: {HdfPath}, ProjPath: {ProjPath} }}";
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
    }

    public interface IUserSettingsRepo
    {
        Task<UserSettings> GetSettings();
        Task SaveSettings(UserSettings userSettings);
    }

    public sealed class FileSystemUserSettingsRepo : IUserSettingsRepo
    {
        private const string AppName = "HydroExplorer";
        private const string SettingsFileName = "userSettings.json";

        private readonly string _settingsFolderPath = GetSettingsFolderPath();
        private readonly string _settingsFilePath = GetSettingsFilePath();

        private readonly SemaphoreSlim _lock = new(1, 1); // ✅ only one operation at a time


        public async Task<UserSettings> GetSettings()
        {
            await _lock.WaitAsync();
            try
            {
                if (!File.Exists(_settingsFilePath))
                {
                    await SaveSettingsInternal(new UserSettings());
                }

                using var settingsFileStream = new FileStream(
                    _settingsFilePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);

                var deserializedSettings = await JsonSerializer.DeserializeAsync<UserSettings>(settingsFileStream);

                return deserializedSettings ?? throw new InvalidOperationException("Can't deserialize user settings");
            }
            finally
            {
                _lock.Release();
            }
        }


        public async Task SaveSettings(UserSettings userSettings)
        {
            await _lock.WaitAsync();
            try
            {
                await SaveSettingsInternal(userSettings);
            }
            finally
            {
                _lock.Release();
            }
        }




        // ✅ Internal method that doesn't acquire the lock
        private async Task SaveSettingsInternal(UserSettings userSettings)
        {
            if (!Directory.Exists(_settingsFolderPath))
                Directory.CreateDirectory(_settingsFolderPath);

            using var settingsFileStream = new FileStream(
                _settingsFilePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);

            await JsonSerializer.SerializeAsync(settingsFileStream, userSettings);
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