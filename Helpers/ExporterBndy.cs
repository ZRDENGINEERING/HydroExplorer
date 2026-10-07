using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using NetTopologySuite.Operation.Union;
using NetTopologySuite.Precision;
using System.Diagnostics;
using System.IO;
using NetTopologySuite.Operation.OverlayNG;


namespace HydroExplorer.Helpers
{
    internal static class ExporterBndy
    {
        
        public static async Task ExportBNDY(
            string pathSubBasins,
            string pathBNDY,
            string pathTMP,
            IUserSettingsRepo settingsRepo,
            string projKey,
            int? sourceEpsgOverride = null)
        {
            string tmpShp = GetTempShpPath();
            try
            {
                await GeoDissolve(inputShp: pathSubBasins, outputShp: tmpShp);
                string tmpPrj = Path.ChangeExtension(tmpShp, ".prj");

                string srcWkt;
                if (sourceEpsgOverride.HasValue)
                {
                    srcWkt = GISUtil.FetchWkt(sourceEpsgOverride.Value);
                    Debug.WriteLine($"ExportBNDY: using explicit source override EPSG={sourceEpsgOverride.Value}");
                }
                else if (File.Exists(tmpPrj))
                {
                    srcWkt = File.ReadAllText(tmpPrj);
                }
                else
                {
                    // No .prj carried over from the dissolve step — resolve via the
                    // same cached/guessed EPSG flow XS and river export use, instead
                    // of silently defaulting to a single zone (EPSG:2278), which can
                    // place the result hundreds of miles off for projects outside it.
                    int? epsg = await GeometryExportCoordinator.ResolveSourceEpsgFromShapefileAsync(
                        settingsRepo,
                        projKey,
                        pathSubBasins,
                        "project boundary");

                    if (epsg is > 0)
                    {
                        srcWkt = GISUtil.FetchWkt(epsg.Value);
                        Debug.WriteLine($"ExportBNDY: no .prj — resolved EPSG:{epsg.Value} via GeometryExportCoordinator.");
                    }
                    else
                    {
                        Debug.WriteLine("ExportBNDY: no .prj and could not resolve/guess a source EPSG — aborting.");
                        return;
                    }
                }

                Debug.WriteLine($"ExportBNDY: srcWkt EPSG={GISUtil.TryGetEpsgFromWkt(srcWkt)}");
                await UtilReproject(
                    inputShp: tmpShp,
                    outputShp: pathBNDY,
                    srcWktOverride: srcWkt
                );
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ExportBNDY error: {ex.Message}");
            }
        }




        public static async Task GeoDissolve(string inputShp, string outputShp, string? dissolveField = null)
        {
            if (inputShp == null) return;
            var factory = new GeometryFactory();
            var reader = new ShapefileDataReader(inputShp, factory);
            string? srcPrj = null;

            string inputPrjPath = Path.ChangeExtension(inputShp, ".prj");

            if (File.Exists(inputPrjPath))
                srcPrj = File.ReadAllText(inputPrjPath);
            var allGeoms = new List<(object key, Geometry geom)>();
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
                var geom = reader.Geometry;
                if (geom != null && !geom.IsEmpty)
                    allGeoms.Add((key, geom));
            }
            reader.Close();
            var pm = new PrecisionModel(10);
            var reducer = new GeometryPrecisionReducer(pm) { ChangePrecisionModel = true };

            var groups = allGeoms
                .AsParallel()
                .GroupBy(x => x.key)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x =>
                    {
                        var geom = x.geom.IsValid ? x.geom : x.geom.Buffer(0);
                        geom = reducer.Reduce(geom);
                        if (geom == null || geom.IsEmpty) return null;
                        if (!geom.IsValid) geom = geom.Buffer(0);
                        return geom;
                    })
                        .Where(geom => geom != null && !geom.IsEmpty)
                        .ToList()
                );
            var outputFeatures = new List<IFeature>();
            foreach (var kvp in groups)
            {
                Debug.WriteLine($"Dissolving group: {kvp.Key} ({kvp.Value.Count} geometries)");
                Geometry? dissolved = null;
                try
                {
                    var collection = new GeometryCollection(kvp.Value.ToArray(), factory);
                    dissolved = OverlayNGRobust.Union((IEnumerable<Geometry>)kvp.Value);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"OverlayNGRobust failed: {ex.Message}, trying iterative...");
                    dissolved = kvp.Value[0];
                    for (int i = 1; i < kvp.Value.Count; i++)
                    {
                        try
                        {
                            dissolved = OverlayNGRobust.Overlay(
                                dissolved,
                                kvp.Value[i],
                                NetTopologySuite.Operation.OverlayNG.OverlayNG.UNION
                            );
                        }
                        catch { Debug.WriteLine($"Skipping geometry {i}"); }
                    }
                }
                if (dissolved != null && !dissolved.IsEmpty)
                {
                    dissolved = NetTopologySuite.Simplify.TopologyPreservingSimplifier
                        .Simplify(dissolved, 0.5);
                    if (!dissolved.IsValid)
                        dissolved = dissolved.Buffer(0);
                }
                if (dissolved == null || dissolved.IsEmpty) continue;
                var attributes = new AttributesTable();
                if (dissolveField != null)
                    attributes.Add(dissolveField, kvp.Key);
                else
                    attributes.Add("id", 1);
                outputFeatures.Add(new Feature(dissolved, attributes));
            }
            var dbaseHeader = new DbaseFileHeader { NumRecords = outputFeatures.Count };
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
            var writer = new ShapefileDataWriter(outputShp, factory) { Header = dbaseHeader };
            writer.Write(outputFeatures);
            if (srcPrj != null)
                File.WriteAllText(Path.ChangeExtension(outputShp, ".prj"), srcPrj);
        }



        public static async Task UtilReproject(
            string inputShp,
            string outputShp,
            string? hecRasProjPath = null,
            string? srcWktOverride = null,
            IUserSettingsRepo? settingsRepo = null,
            string? projKey = null)
        {
            string srcWkt;
            if (srcWktOverride != null)
            {
                srcWkt = srcWktOverride;
                Debug.WriteLine($"UtilReproject: using srcWktOverride, EPSG:{GISUtil.TryGetEpsgFromWkt(srcWkt)}");
            }
            else if (hecRasProjPath != null)
            {
                srcWkt = await GISUtil.FetchHECWkt(hecRasProjPath);
                if (string.IsNullOrEmpty(srcWkt))
                    throw new InvalidOperationException($"Could not resolve projection from HEC-RAS project: {hecRasProjPath}");
                Debug.WriteLine($"UtilReproject: using HEC-RAS WKT, EPSG:{GISUtil.TryGetEpsgFromWkt(srcWkt)}");
            }
            else
            {
                string sidecarPrj = Path.ChangeExtension(inputShp, ".prj");
                if (File.Exists(sidecarPrj))
                {
                    srcWkt = File.ReadAllText(sidecarPrj);
                    Debug.WriteLine($"UtilReproject: using sidecar .prj, EPSG:{GISUtil.TryGetEpsgFromWkt(srcWkt)}");
                }
                else if (settingsRepo != null && !string.IsNullOrEmpty(projKey))
                {
                    // No sidecar .prj and no explicit override — resolve via the same
                    // cached/guessed EPSG flow as XS/river/BNDY export, instead of
                    // silently defaulting to a single zone.
                    int? epsg = await GeometryExportCoordinator.ResolveSourceEpsgFromShapefileAsync(
                        settingsRepo, projKey, inputShp, "reprojected shapefile");

                    if (epsg is > 0)
                    {
                        srcWkt = GISUtil.FetchWkt(epsg.Value);
                        Debug.WriteLine($"UtilReproject: no sidecar .prj — resolved EPSG:{epsg.Value} via GeometryExportCoordinator.");
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            $"UtilReproject: no sidecar .prj for '{inputShp}' and could not resolve/guess a source EPSG.");
                    }
                }
                else
                {
                    throw new InvalidOperationException(
                        $"UtilReproject: no sidecar .prj for '{inputShp}', no override, and no settingsRepo/projKey supplied to resolve one.");
                }
            }
            string tgtWkt = GISUtil.FetchWkt(4326);
            var transform = GISUtil.CreateTransformation(srcWkt, tgtWkt);
            var reader = new ShapefileDataReader(inputShp, GeometryFactory.Default);
            var factory = new GeometryFactory();

            var features = new List<IFeature>();
            bool loggedSanityCheck = false;

            while (reader.Read())
            {
                var geom = reader.Geometry;

                if (!loggedSanityCheck)
                {
                    // One-time sanity-check log of the first feature's centroid before
                    // and after reprojection — previously done via a separate
                    // reader.Read() + reader.Reset() priming pass before the main loop,
                    // but DbaseFileHeader only allows its encoding to be set once, and
                    // that priming sequence triggered it twice, throwing "Setting the
                    // encoding is only allowed once...". Folded into the first loop
                    // iteration instead, since it needs no separate read/reset.
                    var testRaw = geom.EnvelopeInternal.Centre;
                    var testReprojected = GISUtil.Reproject(testRaw, transform);
                    Debug.WriteLine($"UtilReproject centre raw:         {testRaw.X:F4}, {testRaw.Y:F4}");
                    Debug.WriteLine($"UtilReproject centre reprojected: {testReprojected.X:F6}, {testReprojected.Y:F6}");
                    loggedSanityCheck = true;
                }

                Geometry reprojected;
                if (geom is Point)
                {
                    reprojected = factory.CreatePoint(GISUtil.Reproject(geom.Coordinate, transform));
                }
                else if (geom is LineString)
                {
                    var coords = geom.Coordinates.Select(c => GISUtil.Reproject(c, transform)).ToArray();
                    reprojected = factory.CreateLineString(coords);
                }
                else if (geom is Polygon pg)
                {
                    var shell = pg.ExteriorRing.Coordinates
                        .Select(c => GISUtil.Reproject(c, transform)).ToArray();
                    if (!shell.First().Equals2D(shell.Last()))
                        shell = [.. shell, shell.First()];
                    var holes = pg.Holes.Select(hole =>
                    {
                        var hc = hole.Coordinates.Select(c => GISUtil.Reproject(c, transform)).ToArray();
                        if (!hc.First().Equals2D(hc.Last()))
                            hc = [.. hc, hc.First()];
                        return factory.CreateLinearRing(hc);
                    }).ToArray();
                    reprojected = factory.CreatePolygon(factory.CreateLinearRing(shell), holes);
                }
                else if (geom is MultiPolygon mp)
                {
                    var polys = mp.Geometries.Cast<Polygon>().Select(pg2 =>
                    {
                        var coords = pg2.ExteriorRing.Coordinates
                            .Select(c => GISUtil.Reproject(c, transform)).ToArray();
                        if (!coords.First().Equals2D(coords.Last()))
                            coords = [.. coords, coords.First()];
                        return factory.CreatePolygon(coords);
                    }).ToArray();
                    reprojected = factory.CreateMultiPolygon(polys);
                }
                else
                {
                    reprojected = geom;
                }
                var attributes = new AttributesTable();
                for (int i = 0; i < reader.DbaseHeader.Fields.Length; i++)
                    attributes.Add(reader.DbaseHeader.Fields[i].Name, reader.GetValue(i + 1));
                features.Add(new Feature(reprojected, attributes));
            }
            var writer = new ShapefileDataWriter(outputShp, GeometryFactory.Default)
            {
                Header = reader.DbaseHeader
            };
            writer.Write(features);
            GISUtil.WriteShpPrj(outputShp, 4326);
        }
        private static string GetTempShpPath()
        {
            return ProjectsFolder.TempShpPath();
        }
    }
}