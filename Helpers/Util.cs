namespace HydroExplorer.Helpers
{
    public static class Util
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

            _ = bool.TryParse(Convert.ToString(appContextBackdropData), out bool disableFluentThemeWindowBackdrop);


            //if (appContextBackdropData != null)
            //{
            //    disableFluentThemeWindowBackdrop = bool.Parse(value: Convert.ToString(appContextBackdropData));
            //}

            return disableFluentThemeWindowBackdrop;
        }

    }
}
