namespace HydroExplorer.Helpers
{
    internal class EventBus
    {
        public static event Action<string>? HdfPathASelected;
        public static event Action<string>? HdfPathBSelected;
        public static void PublishHdfPathA(string path) => HdfPathASelected?.Invoke(path);
        public static void PublishHdfPathB(string path) => HdfPathBSelected?.Invoke(path);

        public static event Action<string[], string>? HdfFileASelected;
        public static void PublishHdfFileA(string[] profiles, string planNameA)
            => HdfFileASelected?.Invoke(profiles, planNameA);

        public static event Action<string[], string>? HdfFileBSelected;
        public static void PublishHdfFileB(string[] profiles, string planNameB)
            => HdfFileBSelected?.Invoke(profiles, planNameB);

        public static event Action<string>? ProjPathSelected;
        public static void PublishProjPath(string path) => ProjPathSelected?.Invoke(path);

        public static event Action<string>? ProjPathChanged;
        public static void PublishProjPathChanged(string projPath) => ProjPathChanged?.Invoke(projPath);

        public static event Action<string>? RunPathSelected;
        public static void PublishRunPath(string path) => RunPathSelected?.Invoke(path);

        public static event Action<string>? HmsPathChanged;
        public static void PublishHmsPathChanged(string path) => HmsPathChanged?.Invoke(path);

        public static event Action? HdfPathChanged;
        public static void PublishHdfPathChanged() => HdfPathChanged?.Invoke();

        public static event Action<string>? ProfileChanged;
        public static void PublishProfileChanged(string profileName) => ProfileChanged?.Invoke(profileName);



        public static event Action<string>? TopTabChanged;
        public static void RaiseTopTabChanged(string tabHeader) => TopTabChanged?.Invoke(tabHeader);
    }
}