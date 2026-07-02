namespace HydroExplorer.Utils
{
    /// <summary>
    /// heclib's native open-file table is a small, fixed-size, per-process
    /// resource shared by every DssReader in the app — not per-call, per-view.
    /// Reducing handles-per-call (see DssHydrographReader/DssHyetographReader
    /// multi-read overloads) helps, but if multiple call sites open a
    /// DssReader concurrently (rapid project switching, a view double-
    /// subscribing to EventBus.DssRunSelected, etc.), the table can still be
    /// exhausted, crashing the process with an unrecoverable Fortran runtime
    /// error ("subscript #1 of array jwrite has value 7 which is greater than
    /// the upper bound of 6").
    ///
    /// This gate serializes DssReader creation app-wide so concurrent native
    /// handles stay well under that limit regardless of caller count. Every
    /// `new DssReader(...)` in the app should be wrapped in Enter()/Exit().
    /// </summary>
    internal static class DssGate
    {
        // Conservative — 1 at a time. heclib opens/reads are fast (ms range
        // per the timing logs), so serializing has negligible UI impact.
        private static readonly SemaphoreSlim _gate = new(1, 1);

        public static void Enter() => _gate.Wait();
        public static void Exit() => _gate.Release();
    }
}
