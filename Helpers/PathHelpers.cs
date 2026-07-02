using System.IO;


namespace HydroExplorer.Helpers
{
    public static class PathHelpers
    {
        public static string GetFileFolderName(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            var normalizedPath = path.Replace('/', '\\');
            var lastIndex = normalizedPath.LastIndexOf('\\');

            return lastIndex <= 0 ? string.Empty : path[(lastIndex + 1)..];
        }

        public static string NormalizeProjKey(string projPath)
        {
            if (string.IsNullOrEmpty(projPath)) return projPath;

            if (projPath.EndsWith(".rasmap", StringComparison.OrdinalIgnoreCase))
            {
                string dir = Path.GetDirectoryName(projPath) ?? string.Empty;
                string stem = Path.GetFileNameWithoutExtension(projPath);

                string candidatePrj = Path.Combine(dir, stem + ".prj");
                if (File.Exists(candidatePrj))
                    return candidatePrj;

                var anyPrj = Directory.Exists(dir)
                    ? Directory.GetFiles(dir, "*.prj").FirstOrDefault(IsHecRasProjectFile)
                    : null;
                if (anyPrj != null) return anyPrj;
            }

            return projPath;
        }

        public static bool IsHecRasProjectFile(string filePath)
        {
            try
            {
                using StreamReader reader = new(filePath);
                var firstLine = reader.ReadLine()?.Trim();
                return firstLine?.StartsWith("Proj Title", StringComparison.OrdinalIgnoreCase) == true;
            }
            catch
            {
                return false;
            }
        }
    }
}
