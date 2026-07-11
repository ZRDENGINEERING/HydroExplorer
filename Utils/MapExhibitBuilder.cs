using Mapsui;
using Mapsui.Rendering;
using Mapsui.Rendering.Skia;
using System.IO;

namespace HydroExplorer.Reports
{
    public static class MapExhibitBuilder
    {
        // extent = null -> renders whatever the map's current viewport already shows.
        // extent != null -> zooms to that extent first (e.g. selected reach/XS bounds)
        // for a reproducible exhibit independent of what the user has panned/zoomed to.
        public static byte[] ExportMapPng(Map map, int width, int height, MRect? extent = null)
        {
            if (extent != null)
                map.Navigator.ZoomToBox(extent);

            var renderer = new MapRenderer();
            using var stream = renderer.RenderToBitmapStream(
                map.Navigator.Viewport, map.Layers, new RenderService());

            return stream.ToArray();
        }
    }
}