using OxyPlot;


namespace HydroExplorer.Themes
{
    public static class OxyColorPalette
    {
        public static readonly Dictionary<string, OxyColor> Colors = new()
    {
        // Texts
        { "TextAxis",        OxyColor.FromArgb(255, 105, 105, 105) },
        { "TextLight",        OxyColor.FromArgb(255, 175, 175, 175) },

        // Blues
        { "SteelBlue",      OxyColor.Parse("#4682B4") },
        { "CornflowerBlue", OxyColor.Parse("#6495ED") },
        { "DodgerBlue",     OxyColor.Parse("#1E90FF") },
        { "NavyBlue",       OxyColor.Parse("#000080") },

        // Reds / Oranges
        { "OrangeRed",      OxyColor.Parse("#FF4500") },
        { "Tomato",         OxyColor.Parse("#FF6347") },
        { "Crimson",        OxyColor.Parse("#DC143C") },
        { "DarkOrange",     OxyColor.Parse("#FF8C00") },

        // Greens
        { "SeaGreen",       OxyColor.Parse("#2E8B57") },
        { "MediumGreen",    OxyColor.Parse("#3CB371") },
        { "OliveDrab",      OxyColor.Parse("#6B8E23") },
        { "Teal",           OxyColor.Parse("#008080") },

        // Grays
        { "DimGray",        OxyColor.FromArgb(100, 105, 105, 105) },
        { "LightGray",      OxyColor.FromArgb(255, 211, 211, 211) },
        { "SlateGray",      OxyColor.Parse("#708090") },

        // Neutrals
        { "White",          OxyColors.White },
        { "Black",          OxyColors.Black },
        { "Transparent",    OxyColors.Transparent },
    };
    }
}
