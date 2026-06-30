namespace HydroExplorer.Helpers
{
    public class MapStateService
    {
        public event Action<ProjectPaths>? PathsReady;
        public ProjectPaths? CurrentPaths { get; private set; }

        public void Publish(ProjectPaths paths)
        {
            CurrentPaths = paths;
            PathsReady?.Invoke(paths);
        }
    }

    public record ProjectPaths(
        string ProjPath,
        string PathXS,
        string PathRiver,
        string PathBNDY,
        string PathHdfA,
        string PathHdfB,
        string PathHMS,
        string PathSubBasins
    );
}