using System.IO;

namespace HydroExplorer.Helpers
{
    /// <summary>
    /// Where HydroExplorer looks for projects, and where it puts its own scratch files.
    ///
    /// The project tree treats each sub-folder of the "projects folder" as one project.
    /// Historically that folder was hardcoded to C:\Temp; it is now a saved user setting
    /// (UserSettings.ProjectsRoot), with C:\Temp kept only as a fallback so existing
    /// installs keep working without any action.
    /// </summary>
    public static class ProjectsFolder
    {
        public const string LegacyDefault = @"C:\Temp";

        /// <summary>
        /// The folder whose sub-folders are treated as projects: the user's saved choice if
        /// it still exists, else C:\Temp if it exists, else empty (nothing chosen yet).
        /// </summary>
        public static string Resolve(UserSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.ProjectsRoot) && Directory.Exists(settings.ProjectsRoot))
                return settings.ProjectsRoot;

            if (Directory.Exists(LegacyDefault))
                return LegacyDefault;

            return string.Empty;
        }

        /// <summary>True if <paramref name="path"/> is the projects folder itself.</summary>
        public static bool IsRoot(string? path, string? root)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root)) return false;

            return Path.TrimEndingDirectorySeparator(path)
                .Equals(Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// App-private scratch folder under the Windows temp directory (created on demand).
        /// Replaces the old hardcoded C:\Temp for temporary shapefiles.
        /// </summary>
        public static string TempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "HydroExplorer");
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>A unique tmp_*.shp path inside <see cref="TempDir"/>.</summary>
        public static string TempShpPath()
            => Path.Combine(TempDir(), $"tmp_{Guid.NewGuid():N}.shp");
    }
}
