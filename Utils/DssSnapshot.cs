using System.IO;

namespace HydroExplorer.Utils
{
    /// <summary>
    /// HEC-DSS's native heclib (wrapped by Hec.Dss) can throw an unrecoverable
    /// Intel Fortran runtime error — not a catchable .NET exception — when
    /// HydroExplorer opens a .dss file that HEC-HMS currently has open/locked.
    /// Snapshotting the file to a temp copy first avoids contending for HMS's
    /// handle entirely, since DssReader only ever touches the copy.
    ///
    /// Tradeoff: the snapshot can be slightly stale if HMS is mid-write, and a
    /// copy taken mid-write could itself be partial/corrupt (fails cleanly as a
    /// catchable managed exception on read, rather than crashing). Callers
    /// should treat a failed read against a snapshot as "try again shortly"
    /// rather than a hard failure.
    /// </summary>
    public static class DssSnapshot
    {
        public static string Create(string dssFilePath)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "HydroExplorer");
            Directory.CreateDirectory(tempDir);

            string tempPath = Path.Combine(tempDir, $"snap_{Guid.NewGuid():N}.dss");

            // HEC-HMS holding the .dss file open for an entire interactive
            // session (not just a brief write) is common and won't clear
            // with retrying — long backoff just stalls the UI for no
            // benefit. Fail fast (3 attempts, ~450ms total) so the caller
            // can show a clear message instead of hanging.
            const int maxRetries = 3;
            Exception? lastError = null;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    using (var src = new FileStream(dssFilePath, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete))
                    using (var dst = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                    {
                        src.CopyTo(dst);
                    }
                    return tempPath;
                }
                catch (IOException ex)
                {
                    lastError = ex;
                    if (attempt < maxRetries)
                        Thread.Sleep(150 * attempt);
                }
            }

            throw new IOException(
                $"Could not snapshot '{dssFilePath}' — file is locked, likely because " +
                "HEC-HMS currently has it open.", lastError);
        }

        public static void Cleanup(string? tempPath)
        {
            if (string.IsNullOrEmpty(tempPath)) return;
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch { /* best-effort — temp dir gets cleaned by OS eventually */ }
        }
    }
}