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

            // HEC-HMS (via heclib) can hold a transient byte-range lock on part
            // of the file mid-read/write — separate from whole-file sharing, so
            // FileShare.ReadWrite alone doesn't avoid it. Observed: rapid combo
            // box selections can outlast a short retry window if HMS is actively
            // computing. 8 retries with growing backoff (200ms → 1600ms, ~7s
            // total worst case) rides out realistic HMS operations without
            // hanging indefinitely.
            const int maxRetries = 8;
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
                        Thread.Sleep(200 * attempt);
                }
            }

            throw new IOException(
                $"Could not snapshot '{dssFilePath}' after {maxRetries} attempts — " +
                "file remained locked (likely HEC-HMS actively computing).", lastError);
        }

        public static void Cleanup(string? tempPath)
        {
            if (string.IsNullOrEmpty(tempPath)) return;
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch { /* best-effort — temp dir gets cleaned by OS eventually */ }
        }
    }
}