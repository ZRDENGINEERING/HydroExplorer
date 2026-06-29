using HydroExplorer.Helpers;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace HydroExplorer.Helpers
{
    /// <summary>
    /// Fallback BNDY.shp generation using the USGS Watershed Boundary Dataset (WBD)
    /// HU12 (subwatershed/local catchment) layer, queried live by point location.
    /// Used when a project has no HMS subbasins shapefile to dissolve into a boundary
    /// (see ExporterBndy), but does have HEC-RAS cross sections (XS.shp) to derive
    /// a representative project location from.
    /// </summary>
    internal static class ExporterNhdBndy
    {
        private const string WbdHu12QueryUrl =
            "https://hydro.nationalmap.gov/arcgis/rest/services/wbd/MapServer/6/query";

        private static readonly HttpClient _client = new()
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        private static readonly GeometryFactory GeomFactory = new();

        /// <summary>
        /// Queries the HU12 catchment containing the centroid of XS.shp's extent,
        /// and writes it as a boundary shapefile in EPSG:4326 (matching BNDY.shp's
        /// usual CRS from ExporterBndy/MapOverView's existing conventions).
        /// Returns false if XS.shp is missing/unreadable or no catchment is found.
        /// </summary>
        public static async Task<bool> ExportBndyFromNhd(string pathXS, string pathBNDY)
        {
            if (string.IsNullOrEmpty(pathXS) || !File.Exists(pathXS))
            {
                System.Diagnostics.Debug.WriteLine("ExporterNhdBndy: XS.shp missing, cannot derive query point.");
                return false;
            }

            // XS.shp is written in EPSG:4326 by ExporterCrossSection — its lon/lat
            // extent centroid can be used directly as the WBD query point.
            (double lon, double lat)? centroid = GetShpExtentCentroid(pathXS);
            if (centroid is null)
            {
                System.Diagnostics.Debug.WriteLine("ExporterNhdBndy: could not read XS.shp extent.");
                return false;
            }

            var (lon, lat) = centroid.Value;

            var catchment = await QueryHu12CatchmentAsync(lat, lon);
            if (catchment is null)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"ExporterNhdBndy: no HU12 catchment found at ({lat:F6}, {lon:F6}).");
                return false;
            }

            WriteBndyShapefile(catchment.Value.polygon, catchment.Value.huc12, catchment.Value.name, pathBNDY);

            System.Diagnostics.Debug.WriteLine(
                $"ExporterNhdBndy: wrote HU12 catchment '{catchment.Value.huc12}' ({catchment.Value.name}) to '{pathBNDY}'.");

            return true;
        }

        private static (double lon, double lat)? GetShpExtentCentroid(string shpPath)
        {
            try
            {
                var reader = new ShapefileDataReader(shpPath, GeomFactory);
                Envelope? extent = null;

                while (reader.Read())
                {
                    var env = reader.Geometry.EnvelopeInternal;
                    extent = extent is null ? env : extent.Copy();
                    extent?.ExpandToInclude(env);
                }
                reader.Close();

                if (extent is null) return null;

                double lon = (extent.MinX + extent.MaxX) / 2;
                double lat = (extent.MinY + extent.MaxY) / 2;
                return (lon, lat);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ExporterNhdBndy: failed reading XS.shp extent — {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Queries the WBD HU12 (Subwatershed) layer for the catchment polygon
        /// containing the given point. Requests output directly in EPSG:4326.
        /// </summary>
        private static async Task<(Geometry polygon, string huc12, string name)?> QueryHu12CatchmentAsync(double lat, double lon)
        {
            string geometry = $"{lon},{lat}";

            string url = WbdHu12QueryUrl
                + "?f=geojson"
                + "&geometryType=esriGeometryPoint"
                + $"&geometry={Uri.EscapeDataString(geometry)}"
                + "&inSR=4326"
                + "&outSR=4326"
                + "&spatialRel=esriSpatialRelIntersects"
                + "&outFields=huc12,name"
                + "&returnGeometry=true";

            try
            {
                using var response = await _client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"ExporterNhdBndy: WBD query failed with status {response.StatusCode}.");
                    return null;
                }

                using var stream = await response.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(stream);

                if (!doc.RootElement.TryGetProperty("features", out var features) ||
                    features.GetArrayLength() == 0)
                {
                    return null;
                }

                var feature = features[0];
                var props = feature.GetProperty("properties");
                string huc12 = props.TryGetProperty("huc12", out var h) ? h.GetString() ?? string.Empty : string.Empty;
                string name = props.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;

                var geom = feature.GetProperty("geometry");
                var polygon = ParseGeoJsonPolygon(geom);
                if (polygon is null) return null;

                return (polygon, huc12, name);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ExporterNhdBndy: WBD query exception — {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Parses a GeoJSON Polygon or MultiPolygon geometry element into an NTS Geometry.
        /// </summary>
        private static Geometry? ParseGeoJsonPolygon(JsonElement geomElement)
        {
            string type = geomElement.GetProperty("type").GetString() ?? string.Empty;
            var coordinates = geomElement.GetProperty("coordinates");

            if (type.Equals("Polygon", StringComparison.OrdinalIgnoreCase))
            {
                return GeomFactory.CreatePolygon(ParseRing(coordinates[0]));
            }

            if (type.Equals("MultiPolygon", StringComparison.OrdinalIgnoreCase))
            {
                var polygons = new List<Polygon>();
                foreach (var polyCoords in coordinates.EnumerateArray())
                {
                    polygons.Add(GeomFactory.CreatePolygon(ParseRing(polyCoords[0])));
                }
                return GeomFactory.CreateMultiPolygon([.. polygons]);
            }

            System.Diagnostics.Debug.WriteLine($"ExporterNhdBndy: unexpected geometry type '{type}'.");
            return null;
        }

        private static LinearRing ParseRing(JsonElement ringCoords)
        {
            var coords = new List<Coordinate>();
            foreach (var pt in ringCoords.EnumerateArray())
            {
                double x = pt[0].GetDouble();
                double y = pt[1].GetDouble();
                coords.Add(new Coordinate(x, y));
            }

            if (!coords[0].Equals2D(coords[^1]))
                coords.Add(coords[0]);

            return GeomFactory.CreateLinearRing([.. coords]);
        }

        private static void WriteBndyShapefile(Geometry polygon, string huc12, string name, string outputShpPath)
        {
            var attributes = new AttributesTable
            {
                { "huc12", huc12 },
                { "name", name }
            };

            var feature = new Feature(polygon, attributes);

            var header = ShapefileDataWriter.GetHeader(feature, 1);
            var writer = new ShapefileDataWriter(outputShpPath, GeomFactory) { Header = header };
            writer.Write([feature]);

            GISUtil.WriteShpPrj(outputShpPath, 4326);
        }
    }
}