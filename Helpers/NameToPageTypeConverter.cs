using System.Reflection;

namespace HydroExplorer.Helpers;



public sealed class NameToPageTypeConverter
{
    private static readonly Type[] PageTypes = [.. Assembly
        .GetExecutingAssembly()
        .GetTypes()
        .Where(t => t.Namespace?.StartsWith("Wpf.Ui.Gallery.Views.Pages") ?? false)];

    public static Type? Convert(string pageName)
    {
        pageName = pageName.Trim().ToLower() + "page";

        return PageTypes.FirstOrDefault(singlePageType => singlePageType.Name.ToLower() == pageName);
    }
}
