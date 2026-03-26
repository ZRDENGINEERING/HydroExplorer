namespace HydroExplorer.Helpers
{
    public static class Utility
    {
        public static bool IsWindows11OrGreater()
        {
            var os = Environment.OSVersion;
            var version = os.Version;

            return (version.Major >= 10 && version.Build >= 22000);
        }

        public static bool IsBackdropSupported()
        {
            var os = Environment.OSVersion;
            var version = os.Version;

            return version.Major >= 10 && version.Build >= 22621;
        }

        public static bool IsBackdropDisabled()
        {
            var appContextBackdropData = AppContext.GetData("Switch.System.Windows.Appearance.DisableFluentThemeWindowBackdrop");
            bool disableFluentThemeWindowBackdrop = false;

            if (appContextBackdropData != null)
            {
                disableFluentThemeWindowBackdrop = bool.Parse(value: Convert.ToString(appContextBackdropData));
            }

            return disableFluentThemeWindowBackdrop;
        }

    }
}
