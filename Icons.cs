using System.Windows;

namespace HydroExplorer
{
    public static class Icons
    {
        public static ComponentResourceKey HydroExplorer => new(typeof(Icons), "HydroExplorer");

        public static ComponentResourceKey Error => new(typeof(Icons), "Error");

        public static ComponentResourceKey WindowMinimize => new(typeof(Icons), "WindowMinimize");

        public static ComponentResourceKey WindowMaximize => new(typeof(Icons), "WindowMaximize");

        public static ComponentResourceKey WindowRestore => new(typeof(Icons), "WindowRestore");

        public static ComponentResourceKey WindowClose => new(typeof(Icons), "WindowClose");
    }
}