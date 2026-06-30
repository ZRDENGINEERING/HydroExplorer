using System.IO;
using System.Text.Json;

namespace HydroExplorer.Helpers
{
    public class UserSettings
    {
        public string LastProjPath { get; set; } = string.Empty;
        public string ProjName { get; set; } = string.Empty;
        public string ProjPath { get; set; } = string.Empty;

        public Dictionary<string, ProjectSettings> Projects { get; set; } = [];
        public Dictionary<string, DateTime> HmsProjects { get; set; } = [];
        public Dictionary<string, ShpPathEntry> ShpPaths { get; set; } = [];

        public IEnumerable<KeyValuePair<string, ProjectSettings>> RecentProjects =>
            Projects.Reverse().Take(10);

        public IEnumerable<KeyValuePair<string, ShpPathEntry>> GeometryPaths =>
            ShpPaths.Reverse().Take(20);

        public override string ToString() => $"{{ ProjPath: \"{ProjPath}\" }}";
    }


    public class ShpPathEntry
    {
        public DateTime LastOpened { get; set; }
        public string LayerType { get; set; } = string.Empty;
    }


    public class ProjectSettings
    {
        public string ProjName { get; set; } = string.Empty;
        public string ProjRoot { get; set; } = string.Empty;
        public string ProjPath { get; set; } = string.Empty;
        public string ModelName { get; set; } = string.Empty;
        public string DssPath { get; set; } = string.Empty;
        public string HdfPathA { get; set; } = string.Empty;
        public string HdfPathB { get; set; } = string.Empty;
        public string PlanNameA { get; set; } = string.Empty;
        public string PlanNameB { get; set; } = string.Empty;
        public string ProName { get; set; } = string.Empty;
        public string HmsPath { get; set; } = string.Empty;
        public List<string> SelectedReaches { get; set; } = [];
        public DateTime LastOpened { get; set; } = DateTime.MinValue;
        public int OpenOrder { get; set; } = 0;

        // Resolved Spatial folder paths — written by MapOverView.BuildPaths once known,
        // since the Spatial folder sits next to the project file, not necessarily at ProjRoot
        // (ProjRoot can be several directories higher for deeply nested projects).
        public string SpatialBndyPath { get; set; } = string.Empty;
        public string SpatialXsPath { get; set; } = string.Empty;
        public string SpatialRiverPath { get; set; } = string.Empty;

        // Source EPSG for HEC-RAS geometry exports (XS/river centerlines) when the
        // project has no .prj sidecar folder. Set once — either guessed via
        // GISUtil.GuessTexasStatePlaneZone and confirmed by the user, or resolved
        // from an actual .prj — so later exports (e.g. river after XS) don't have
        // to re-guess or re-prompt for the same project.
        public int? SourceEpsg { get; set; } = null;

        // Set true if the user declines the NHD HU12 boundary fallback prompt,
        // so we don't keep re-asking every time this project is opened.
        public bool NhdBoundaryDeclined { get; set; } = false;

        // USGS nearest-gage — static identity/location, fetched once ever per project
        public string GageSiteNo { get; set; } = string.Empty;
        public string GageName { get; set; } = string.Empty;
        public double GageLat { get; set; } = 0;
        public double GageLon { get; set; } = 0;
        public double GageDistanceMiles { get; set; } = 0;
        public string GageHucCode { get; set; } = string.Empty;
        public double? GageDrainageAreaSqMi { get; set; } = null;
    }


    public class GeometryPathEntry
    {
        public string ShpPath { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Directory { get; set; } = string.Empty;
        public string LayerType { get; set; } = string.Empty;
        public DateTime LastOpened { get; set; }
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
        public string ProjName { get; set; } = string.Empty;
        public string ProjPath { get; set; } = string.Empty;
        public DateTime LastOpened { get; set; }
        public int OpenOrder { get; set; }
        public string ProjectType { get; set; } = "RAS";
        public string? HmsRunFile { get; set; }
        public bool IsActive { get; set; }
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

                var fileInfo = new FileInfo(_settingsFilePath);
                if (fileInfo.Length == 0)
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