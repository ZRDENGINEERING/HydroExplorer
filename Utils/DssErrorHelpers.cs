

namespace HydroExplorer.Utils
{
    /// <summary>
    /// Centralizes recognition of known, user-meaningful DSS read failures so
    /// every reader (DssHydrographReader, DssHyetographReader, DssDataService,
    /// TabChartViewModel.LoadDssDataAsync) reports the same message instead of
    /// each repeating its own string match.
    /// </summary>
    internal static class DssErrorHelpers
    {
        public static bool IsUnsupportedVersion(Exception ex) =>
            ex.Message.Contains("DSS version 7", StringComparison.OrdinalIgnoreCase);

        public const string UnsupportedVersionMessage =
            "This project's DSS file is an older format (DSS-6) and can't be read. " +
            "Convert it to DSS-7 in HEC-HMS or HEC-DSSVue to view charts.";
    }
}