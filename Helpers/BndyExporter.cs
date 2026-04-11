using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using NetTopologySuite.Operation.Union;
using NetTopologySuite.Operation.Valid;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;
using System.IO;
using System.Net.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;



namespace HydroExplorer.Helpers
{
    internal class BndyExporter
    {
        static readonly HttpClient client = new();
        private string _srcWkt = string.Empty;
        private string _tgtWkt = string.Empty;


        public async Task GeoDissolve(string inputShp, string outputShp, string dissolveField = null)
        {

            if (inputShp == null) return;

            var factory = new GeometryFactory();
            var reader = new ShapefileDataReader(inputShp, factory);
            var prjPath = Path.ChangeExtension(inputShp, ".prj");
            string prjText = File.Exists(prjPath) ? File.ReadAllText(prjPath) : null;

            // Read all geometries, optionally grouped by field
            var groups = new Dictionary<object, List<Geometry>>();
            string fieldName = dissolveField ?? "__all__";

            while (reader.Read())
            {
                object key = "__all__";

                if (dissolveField != null)
                {
                    int fieldIndex = reader.DbaseHeader.Fields
                        .Select((f, i) => (f, i))
                        .FirstOrDefault(x => x.f.Name.Equals(dissolveField, StringComparison.OrdinalIgnoreCase)).i;

                    key = reader.GetValue(fieldIndex + 1) ?? DBNull.Value;
                }

                if (!groups.ContainsKey(key))
                    groups[key] = new List<Geometry>();

                var geom = reader.Geometry;
                if (geom != null && !geom.IsEmpty)
                {
                    // Clean geometry before adding
                    if (!geom.IsValid)
                    {
                        geom = geom.Buffer(0);
                    }
                    groups[key].Add(geom);
                }
            }

            reader.Close();

            // Union each group
            var outputFeatures = new List<IFeature>();

            foreach (var kvp in groups)
            {
                System.Diagnostics.Debug.WriteLine($"Dissolving group: {kvp.Key} ({kvp.Value.Count} geometries)");

                Geometry dissolved = null;

                try
                {
                    dissolved = CascadedPolygonUnion.Union(kvp.Value);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"CascadedPolygonUnion failed: {ex.Message}, trying iterative union...");

                    dissolved = kvp.Value[0];
                    for (int i = 1; i < kvp.Value.Count; i++)
                    {
                        try
                        {
                            dissolved = dissolved.Union(kvp.Value[i]);
                        }
                        catch
                        {
                            try
                            {
                                dissolved = dissolved.Union(kvp.Value[i].Buffer(0));
                            }
                            catch
                            {
                                System.Diagnostics.Debug.WriteLine($"Skipping geometry {i} in group {kvp.Key}");
                            }
                        }
                    }
                }



                double tolerance = 1.0;
                dissolved = dissolved.Buffer(tolerance).Buffer(-tolerance);

                //Console.WriteLine($"Dissolved and cleaned: {dissolved.NumGeometries} geometry/geometries");



                if (dissolved == null || dissolved.IsEmpty)
                {
                    System.Diagnostics.Debug.WriteLine($"Skipping empty result for group: {kvp.Key}");
                    continue;
                }

                var attributes = new AttributesTable();

                // Use consistent field name regardless of dissolve mode
                if (dissolveField != null)
                    attributes.Add(dissolveField, kvp.Key);
                else
                    attributes.Add("id", 1);  // matches header below

                outputFeatures.Add(new Feature(dissolved, attributes));
            }




            // Explicitly verify feature count before writing
            //System.Diagnostics.Debug.WriteLine($"Writing {outputFeatures.Count} dissolved features...");

            var dbaseHeader = new DbaseFileHeader();
            dbaseHeader.NumRecords = outputFeatures.Count;  // set record count explicitly

            if (dissolveField != null)
            {
                var originalField = reader.DbaseHeader.Fields
                    .FirstOrDefault(f => f.Name.Equals(dissolveField, StringComparison.OrdinalIgnoreCase));

                if (originalField != null)
                    dbaseHeader.AddColumn(originalField.Name, originalField.DbaseType, originalField.Length, originalField.DecimalCount);
                else
                    dbaseHeader.AddColumn(dissolveField, 'C', 50, 0);
            }
            else
            {
                dbaseHeader.AddColumn("id", 'N', 10, 0);
            }







            // Delete existing output files first
            //string[] extensions = { ".shp", ".shx", ".dbf", ".prj", ".cpg" };
            //foreach (var ext in extensions)
            //{
            //    string path = Path.ChangeExtension(outputShp, ext);
            //    if (File.Exists(path))
            //    {
            //        File.Delete(path);
            //        System.Diagnostics.Debug.WriteLine($"Deleted existing: {path}");
            //    }
            //}

            // Then write
            var writer = new ShapefileDataWriter(outputShp, new GeometryFactory())
            {
                Header = dbaseHeader
            };
            writer.Write(outputFeatures);

            if (prjText != null)
                File.WriteAllText(Path.ChangeExtension(outputShp, ".prj"), prjText);

            //Console.WriteLine($"Write complete. Check {outputShp}");

        }








        public static void DissolveShapefile(string inputShp, string outputShp)
        {
            var factory = new GeometryFactory();
            var reader = new ShapefileDataReader(inputShp, factory);
            string prjText = File.Exists(Path.ChangeExtension(inputShp, ".prj"))
                ? File.ReadAllText(Path.ChangeExtension(inputShp, ".prj"))
                : null;

            // Read all geometries
            var geometries = new List<Geometry>();
            while (reader.Read())
            {
                var geom = reader.Geometry;
                if (geom == null || geom.IsEmpty) continue;
                if (!geom.IsValid) geom = geom.Buffer(0);
                geometries.Add(geom);
            }
            reader.Close();

            //Console.WriteLine($"Read {geometries.Count} geometries");

            // Dissolve
            Geometry dissolved = null;
            try
            {
                dissolved = CascadedPolygonUnion.Union(geometries);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CascadedPolygonUnion failed: {ex.Message}, trying iterative...");
                dissolved = geometries[0];
                for (int i = 1; i < geometries.Count; i++)
                {
                    try { dissolved = dissolved.Union(geometries[i]); }
                    catch { dissolved = dissolved.Union(geometries[i].Buffer(0)); }
                }
            }

            Console.WriteLine($"Dissolved into {dissolved.NumGeometries} geometry/geometries");

            // Delete existing output files
            foreach (var ext in new[] { ".shp", ".shx", ".dbf", ".prj", ".cpg" })
            {
                string path = Path.ChangeExtension(outputShp, ext);
                if (File.Exists(path)) File.Delete(path);
            }

            // Write output
            var attributes = new AttributesTable();
            attributes.Add("id", 1);

            var features = new List<IFeature> { new Feature(dissolved, attributes) };

            var dbaseHeader = new DbaseFileHeader();
            dbaseHeader.NumRecords = 1;
            dbaseHeader.AddColumn("id", 'N', 10, 0);

            var writer = new ShapefileDataWriter(outputShp, factory) { Header = dbaseHeader };
            writer.Write(features);

            if (prjText != null)
                File.WriteAllText(Path.ChangeExtension(outputShp, ".prj"), prjText);

            //Console.WriteLine($"Done. Written to {outputShp}");
        }











        public async Task UtilReproject(string inputShp, string outputShp)
        {
            var reader = new ShapefileDataReader(inputShp, GeometryFactory.Default);
            var csFactory = new CoordinateSystemFactory();
            var ctFactory = new CoordinateTransformationFactory();
          


            int srcEPSG = 2277;
            await FetchWktSrc(srcEPSG);
            var sourceCrs = csFactory.CreateFromWkt(_srcWkt);

            int tgtEPSG = 4326;
            await FetchWktTgt(tgtEPSG);
            var targetCrs = csFactory.CreateFromWkt(_tgtWkt);

            var transform = ctFactory.CreateFromCoordinateSystems(sourceCrs, targetCrs);

            var header = new DbaseFileHeader();
            header = reader.DbaseHeader;
            var features = new List<IFeature>();

            while (reader.Read())
            {
                var geom = reader.Geometry;
                var coords = geom.Coordinates.Select(c =>
                {
                    try
                    {
                        double[] pt = transform.MathTransform.Transform(new[] { c.X, c.Y });

                        if (double.IsNaN(pt[0]) || double.IsNaN(pt[1]))
                        {
                            System.Diagnostics.Debug.WriteLine($"NaN result for input: X={c.X}, Y={c.Y}");
                            return new Coordinate(c.X, c.Y); // fall back to original
                        }

                        return new Coordinate(pt[0], pt[1]);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Transform failed: X={c.X}, Y={c.Y} — {ex.Message}");
                        return new Coordinate(c.X, c.Y);
                    }
                }).ToArray();

                // Force close the ring if first and last points don't match
                if (!coords.First().Equals2D(coords.Last()))
                    coords = coords.Append(coords.First()).ToArray();

                var factory = new GeometryFactory();

                Geometry reprojected;
                if (geom is Point)
                {
                    reprojected = factory.CreatePoint(coords[0]);
                }
                else if (geom is LineString)
                {
                    reprojected = factory.CreateLineString(coords);
                }

                else if (geom is Polygon pg)
                {
                    // Close exterior ring
                    var shell = coords;
                    if (!shell.First().Equals2D(shell.Last()))
                        shell = shell.Append(shell.First()).ToArray();

                    var exterior = factory.CreateLinearRing(shell);

                    // Handle holes
                    var holes = pg.Holes.Select(hole =>
                    {
                        var holeCoords = hole.Coordinates.Select(c =>
                        {
                            double[] pt = transform.MathTransform.Transform(new[] { c.X, c.Y });
                            return new Coordinate(pt[0], pt[1]);
                        }).ToArray();

                        if (!holeCoords.First().Equals2D(holeCoords.Last()))
                            holeCoords = holeCoords.Append(holeCoords.First()).ToArray();

                        return factory.CreateLinearRing(holeCoords);
                    }).ToArray();
                    reprojected = factory.CreatePolygon(exterior, holes);
                }

                else if (geom is MultiPolygon mp)
                {
                    var polys = mp.Geometries.Cast<Polygon>().Select(pg2 =>
                    {
                        var transformed = pg2.Coordinates.Select(c =>
                        {
                            double[] pt = transform.MathTransform.Transform(new[] { c.X, c.Y });
                            return new Coordinate(pt[0], pt[1]);
                        }).ToArray();

                        if (!transformed.First().Equals2D(transformed.Last()))
                            transformed = transformed.Append(transformed.First()).ToArray();

                        return factory.CreatePolygon(transformed);
                    }).ToArray();

                    reprojected = factory.CreateMultiPolygon(polys);
                }
                else
                {
                    reprojected = geom;
                }

                var attributes = new AttributesTable();
                for (int i = 0; i < reader.DbaseHeader.Fields.Length; i++)
                {
                    string name = reader.DbaseHeader.Fields[i].Name;
                    object value = reader.GetValue(i + 1);
                    attributes.Add(name, value);
                }
                features.Add(new Feature(reprojected, attributes));
            }

            var writer = new ShapefileDataWriter("C:\\Temp\\BART_HECRASv410_R2\\Spatial\\BNDY.shp", GeometryFactory.Default)
            {
                Header = reader.DbaseHeader
            };
            writer.Write(features);

            string prjPath = Path.ChangeExtension(outputShp, ".prj");
            File.WriteAllText(prjPath, _tgtWkt);
        }






















        public async Task FetchWktSrc(int epsgCode)
        {
            try
            {
                _srcWkt = await client.GetStringAsync($"https://epsg.io/{epsgCode}.wkt");
            }
            catch (HttpRequestException e)
            {
                System.Diagnostics.Debug.WriteLine("\nException Caught!");
                System.Diagnostics.Debug.WriteLine("Message :{0} ", e.Message);
            }
        }

        public async Task FetchWktTgt(int epsgCode)
        {
            try
            {
                _tgtWkt = await client.GetStringAsync($"https://epsg.io/{epsgCode}.wkt");
            }
            catch (HttpRequestException e)
            {
                System.Diagnostics.Debug.WriteLine("\nException Caught!");
                System.Diagnostics.Debug.WriteLine("Message :{0} ", e.Message);
            }
        }
    }
}
