using System;
using System.Collections.Generic;
using System.Text;


namespace HydroExplorer.Helpers
{

    //public class MapStateService
    //{
    //    public ProjectPaths? CurrentPaths { get; private set; }
    //    public event Action<ProjectPaths>? PathsReady;

    //    public void Publish(ProjectPaths paths)
    //    {
    //        CurrentPaths = paths;
    //        PathsReady?.Invoke(paths);
    //    }
    //}


    public class MapStateService
    {
        public event Action<ProjectPaths>? PathsReady;
        public ProjectPaths? CurrentPaths { get; private set; }


        // Shared paths
        public string? PathXS { get; set; }
        public string? PathBNDY { get; set; }
        public string? PathHdfA { get; set; }
        public string? PathHdfB { get; set; }
        public string? PathHMS { get; set; }
        public string? PathSubBasins { get; set; }

        // Shared state
        public string ActiveProjPath { get; set; } = string.Empty;


        public void Publish(ProjectPaths paths)
        {
            CurrentPaths = paths;
            PathsReady?.Invoke(paths);
        }
    }

    // A plain record to pass around instead of loose strings
    public record ProjectPaths(
        string ProjPath,
        string PathXS,
        string PathCL,
        string PathBNDY,
        string PathHdfA,
        string PathHdfB,
        string PathHMS,
        string PathSubBasins
    );
}
