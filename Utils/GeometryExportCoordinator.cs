using HydroExplorer.Utils;
using PureHDF;
using PureHDF.VOL.Native;
using System.IO;


namespace HydroExplorer.Helpers
{
    /// <summary>
    /// Exports HEC-RAS XS.shp and River.shp from a plan/geometry HDF, sharing a single
    /// CRS resolution path (cached per-project in ProjectSettings.SourceEpsg) so a
    /// project with no .prj sidecar only ever gets its zone guessed once, regardless
    /// of which export runs first.
    ///
    /// Intentionally decoupled from MapOverView/MapView — call this from whatever
    /// owns project-load sequencing (e.g. after MapOverView publishes ProjectPaths,
    /// before MapView is allowed to read XS.shp/River.shp off disk), not from inside
    /// either map control itself.
    /// </summary>
    public static class GeometryExportCoordinator
    {
        public static string? ResolveHdfPath(string? pathHdfA, string? pathHdfB) =>
            !string.IsNullOrEmpty(pathHdfA) && File.Exists(pathHdfA) ? pathHdfA :
            !string.IsNullOrEmpty(pathHdfB) && File.Exists(pathHdfB) ? pathHdfB :
            null;

        /// <summary>
        /// Exports XS.shp if it doesn't already exist. Returns true if the file exists
        /// afterward (whether just-created or already present), false if export was
        /// skipped or failed.
        /// </summary>
        public static async Task<bool> ExportShpXSAsync(
            IUserSettingsRepo settingsRepo,
            string projKey,
            string? pathHdfA,
            string? pathHdfB,
            string? pathXS)
        {
            try
            {
                string? hdfPath = ResolveHdfPath(pathHdfA, pathHdfB);
                if (string.IsNullOrEmpty(hdfPath)) return false;
                if (string.IsNullOrEmpty(pathXS)) return false;
                if (File.Exists(pathXS)) return true;

                System.Diagnostics.Debug.WriteLine($"GeometryExportCoordinator: XS FILE DOES NOT EXIST — CREATING @ {pathXS}");

                string projDir = Path.GetDirectoryName(hdfPath) ?? string.Empty;

                bool exported = await ExporterXS.ExportXSToShp(
                    projPath: projDir,
                    hdfPath: hdfPath,
                    outputShpPath: pathXS);

                if (exported) return true;

                int? epsg = await ResolveSourceEpsgAsync(
                    settingsRepo,
                    projKey,
                    hdfPath,
                    "/Geometry/Cross Sections",
                    "Polyline Points",
                    "cross sections");

                if (epsg is not > 0) return false;

                return await ExporterXS.ExportXSToShp(
                    projPath: projDir,
                    hdfPath: hdfPath,
                    outputShpPath: pathXS,
                    sourceEpsgOverride: epsg);
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("GeometryExportCoordinator: ExportShpXSAsync cancelled.");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GeometryExportCoordinator: ExportShpXSAsync error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Exports River.shp if it doesn't already exist. Returns true if the file
        /// exists afterward (whether just-created or already present), false if
        /// export was skipped or failed.
        /// </summary>
        public static async Task<bool> ExportShpRiverAsync(
            IUserSettingsRepo settingsRepo,
            string projKey,
            string? pathHdfA,
            string? pathHdfB,
            string? pathRiver)
        {

            try
            {
                string? hdfPath = ResolveHdfPath(pathHdfA, pathHdfB);

                if (string.IsNullOrEmpty(hdfPath)) return false;
                if (string.IsNullOrEmpty(pathRiver)) return false;
                if (File.Exists(pathRiver)) return true;

                System.Diagnostics.Debug.WriteLine($"GeometryExportCoordinator try: CL FILE DOES NOT EXIST — CREATING @ {pathRiver}");

                string projDir = Path.GetDirectoryName(hdfPath) ?? string.Empty;

                bool exported = await ExporterRiver.ExportRiverToShp(
                    projPath: projDir,
                    hdfPath: hdfPath,
                    outputShpPath: pathRiver);

                if (exported) return true;

                // Reuses the EPSG already guessed/cached during XS export for this
                // project, if any — see ResolveSourceEpsgAsync.
                int? epsg = await ResolveSourceEpsgAsync(
                    settingsRepo,
                    projKey,
                    hdfPath,
                    "/Geometry/River Centerlines",
                    "Polyline Points",
                    "river centerlines");

                if (epsg is not > 0) return false;

                return await ExporterRiver.ExportRiverToShp(
                    projPath: projDir,
                    hdfPath: hdfPath,
                    outputShpPath: pathRiver,
                    sourceEpsgOverride: epsg);
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("GeometryExportCoordinator: ExportShpRiverAsync cancelled.");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GeometryExportCoordinator: ExportShpRiverAsync error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Exports BNDY.shp by dissolving the subbasins shapefile, if BNDY
        /// doesn't already exist or is stale relative to subbasins. Falls back
        /// to prompting for an NHD-derived boundary when no subbasins
        /// shapefile is available at all. Returns true if a boundary exists
        /// on disk afterward (created just now or already present), false
        /// otherwise.
        /// </summary>
        public static async Task<bool> ExportShpBndyAsync(
            IUserSettingsRepo settingsRepo,
            string projKey,
            string? pathSubBasins,
            string? pathBNDY,
            string? pathXS)
        {
            try
            {
                if (string.IsNullOrEmpty(pathSubBasins) || !File.Exists(pathSubBasins))
                {
                    if (!string.IsNullOrEmpty(pathBNDY) && File.Exists(pathBNDY)) return true;
                    if (string.IsNullOrEmpty(pathXS) || !File.Exists(pathXS)) return false;

                    return await PromptForNhdBoundaryAsync(settingsRepo, projKey, pathXS, pathBNDY);
                }

                if (string.IsNullOrEmpty(pathBNDY)) return false;

                if (File.Exists(pathBNDY) && File.Exists(pathSubBasins))
                {
                    var newer = GetNewerFile(pathBNDY, pathSubBasins);
                    if (newer?.FullName == pathBNDY) return true; // BNDY already up to date
                }

                string pathTMP = GetTempShpPath();

                try
                {
                    await ExporterBndy.ExportBNDY(
                        pathSubBasins: pathSubBasins,
                        pathTMP: pathTMP,
                        pathBNDY: pathBNDY,
                        settingsRepo: settingsRepo,
                        projKey: projKey
                    );
                    return File.Exists(pathBNDY);
                }
                finally
                {
                    GISUtil.DeleteShapefileIfExists(pathTMP);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GeometryExportCoordinator: ExportShpBndyAsync error: {ex.Message}");
                return false;
            }
        }

        private static async Task<bool> PromptForNhdBoundaryAsync(
            IUserSettingsRepo settingsRepo, string projKey, string pathXS, string? pathBNDY)
        {
            if (string.IsNullOrEmpty(pathBNDY)) return false;

            var settings = await settingsRepo.GetSettings();

            if (string.IsNullOrEmpty(projKey) ||
                !settings.Projects.TryGetValue(projKey, out var projSettings))
                return false;

            if (projSettings.NhdBoundaryDeclined) return false;

            var result = await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                System.Windows.MessageBox.Show(
                    "No project boundary (BNDY.shp) is available for this project, and no HMS " +
                    "subbasins shapefile was found to derive one from.\n\n" +
                    "Would you like to fetch a watershed boundary from the USGS National Hydrography " +
                    "Dataset based on the cross-section location?",
                    "No Project Boundary Found",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question));

            if (result != System.Windows.MessageBoxResult.Yes)
            {
                projSettings.NhdBoundaryDeclined = true;
                await settingsRepo.SaveSettings(settings);
                return false;
            }

            bool success = await ExporterNhdBndy.ExportBndyFromNhd(pathXS, pathBNDY);

            if (!success)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    System.Windows.MessageBox.Show(
                        "Could not retrieve a watershed boundary from the USGS service. " +
                        "Check your network connection and try again, or set a boundary manually.",
                        "NHD Boundary Fetch Failed",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning));
            }

            return success;
        }

        private static FileInfo? GetNewerFile(string pathA, string pathB)
        {
            if (pathA == "" && pathB == "") return null;

            var fileA = new FileInfo(pathA);
            var fileB = new FileInfo(pathB);

            if (!fileA.Exists) throw new FileNotFoundException($"File not found: {pathA}");
            if (!fileB.Exists) throw new FileNotFoundException($"File not found: {pathB}");

            return fileA.LastWriteTime > fileB.LastWriteTime ? fileA : fileB;
        }

        private static string GetTempShpPath()
        {
            string tmpDir = @"C:\Temp";
            string uniqueName = $"tmp_{Guid.NewGuid():N}.shp";
            return Path.Combine(tmpDir, uniqueName);
        }

        /// <summary>
        /// Convenience: runs XS, River, then BNDY export in sequence (XS
        /// first, since its guess is what gets cached for River/BNDY to
        /// reuse; BNDY last, since it needs XS as an NHD-fallback centroid
        /// source). Call this once per project load, after paths are known
        /// and before any UI reads the resulting .shp files off disk.
        /// </summary>
        public static async Task ExportAllAsync(
            IUserSettingsRepo settingsRepo,
            string projKey,
            string? pathHdfA,
            string? pathHdfB,
            string? pathXS,
            string? pathRiver,
            string? pathSubBasins = null,
            string? pathBNDY = null)
        {
            await ExportShpXSAsync(settingsRepo, projKey, pathHdfA, pathHdfB, pathXS);
            await ExportShpRiverAsync(settingsRepo, projKey, pathHdfA, pathHdfB, pathRiver);

            if (pathSubBasins != null || pathBNDY != null)
                await ExportShpBndyAsync(settingsRepo, projKey, pathSubBasins, pathBNDY, pathXS);
        }

        /// <summary>
        /// Reads the raw (unprojected) extent of a polyline-points dataset directly
        /// from the HDF, for CRS zone-guessing when no .prj sidecar/folder exists to
        /// tell us what those coordinates are in. groupPath/datasetName are e.g.
        /// "/Geometry/Cross Sections" + "Polyline Points", or
        /// "/Geometry/River Centerlines" + "Polyline Points".
        /// </summary>
        private static NetTopologySuite.Geometries.Envelope? TryReadHdfPolylineExtent(string hdfPath, string groupPath, string datasetName)
        {
            try
            {
                using var file = HecRasHdfReader.OpenHdf(hdfPath);
                var group = file.Group(groupPath);
                double[] allPoints = group.Dataset(datasetName).Read<double[]>();

                if (allPoints.Length < 2) return null;

                double minX = double.MaxValue, maxX = double.MinValue;
                double minY = double.MaxValue, maxY = double.MinValue;

                for (int i = 0; i < allPoints.Length; i += 2)
                {
                    double x = allPoints[i];
                    double y = allPoints[i + 1];
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }

                return new NetTopologySuite.Geometries.Envelope(minX, maxX, minY, maxY);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GeometryExportCoordinator: TryReadHdfPolylineExtent('{groupPath}/{datasetName}') failed — {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Resolves the source EPSG to use when a HEC-RAS project has no .prj sidecar
        /// folder — checked once per project and shared across exporters (XS, river,
        /// BNDY) so the zone is guessed only once, then cached in
        /// ProjectSettings.SourceEpsg for the lifetime of the project. The HDF and
        /// shapefile overloads below both funnel into this; callers shouldn't need
        /// to call this directly.
        ///
        /// Confirmation is currently silenced — the guessed zone is auto-accepted
        /// rather than prompted via MessageBox. The prompt is kept below, commented,
        /// in case we want to re-enable it later.
        ///
        /// Returns null if no project key is resolvable or no zone can be guessed
        /// from the raw coordinates.
        /// </summary>
        private static async Task<int?> ResolveSourceEpsgAsync(
            IUserSettingsRepo settingsRepo,
            string projKey,
            Func<NetTopologySuite.Geometries.Envelope?> getRawExtent,
            string featureLabel)
        {
            var settings = await settingsRepo.GetSettings();

            if (!string.IsNullOrEmpty(projKey) &&
                settings.Projects.TryGetValue(projKey, out var existing) &&
                existing.SourceEpsg is > 0)
            {
                //System.Diagnostics.Debug.WriteLine(
                //    $"GeometryExportCoordinator: using cached EPSG:{existing.SourceEpsg} for project '{projKey}'.");
                return existing.SourceEpsg;
            }

            var rawExtent = getRawExtent();
            int guessedEpsg = rawExtent != null ? GISUtil.GuessTexasStatePlaneZone(rawExtent) : -1;

            if (guessedEpsg <= 0)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"GeometryExportCoordinator: no .prj folder and could not guess a Texas State Plane zone — skipping {featureLabel}.");
                return null;
            }

            // Silenced: previously prompted the user to confirm the guessed zone via
            // MessageBox. Now auto-accepts the guess and proceeds — kept here,
            // commented, in case we want to re-enable confirmation later.
            //
            // var confirm = await Application.Current.Dispatcher.InvokeAsync(() => MessageBox.Show(
            //     $"This HEC-RAS project has no projection (.prj) file. Based on its coordinates, " +
            //     $"it's likely EPSG:{guessedEpsg}. Use this projection for {featureLabel}?",
            //     "Unknown Projection — Confirm Guess",
            //     MessageBoxButton.YesNo, MessageBoxImage.Question));
            //
            // if (confirm != MessageBoxResult.Yes) return null;

            //System.Diagnostics.Debug.WriteLine(
            //    $"GeometryExportCoordinator: no .prj folder for {featureLabel} — auto-accepting guessed EPSG:{guessedEpsg} without confirmation.");

            if (!string.IsNullOrEmpty(projKey))
            {
                if (!settings.Projects.TryGetValue(projKey, out var proj))
                    proj = settings.Projects[projKey] = new ProjectSettings { ProjPath = projKey };

                proj.SourceEpsg = guessedEpsg;
                await settingsRepo.SaveSettings(settings);

                //System.Diagnostics.Debug.WriteLine(
                //    $"GeometryExportCoordinator: cached EPSG:{guessedEpsg} for project '{projKey}'.");
            }

            return guessedEpsg;
        }

        /// <summary>
        /// HDF-backed CRS resolution — used by XS/river export. Reads the raw extent
        /// from the given HDF group/dataset only if no cached EPSG already exists for
        /// this project.
        /// </summary>
        private static Task<int?> ResolveSourceEpsgAsync(
            IUserSettingsRepo settingsRepo,
            string projKey,
            string hdfPath,
            string groupPath,
            string datasetName,
            string featureLabel) =>
            ResolveSourceEpsgAsync(
                settingsRepo,
                projKey,
                () => TryReadHdfPolylineExtent(hdfPath, groupPath, datasetName),
                featureLabel);

        /// <summary>
        /// Shapefile-backed CRS resolution — used by BNDY export, which dissolves a
        /// subbasins shapefile rather than reading an HDF. Shares the same
        /// ProjectSettings.SourceEpsg cache as the HDF overload, so if XS or river
        /// export already resolved a zone for this project, BNDY reuses it without
        /// re-reading anything.
        /// </summary>
        public static Task<int?> ResolveSourceEpsgFromShapefileAsync(
            IUserSettingsRepo settingsRepo,
            string projKey,
            string shapefilePath,
            string featureLabel) =>
            ResolveSourceEpsgAsync(
                settingsRepo,
                projKey,
                () => TryReadShapefileExtent(shapefilePath),
                featureLabel);

        /// <summary>
        /// Reads the raw (unprojected) extent of a shapefile's geometries directly off
        /// disk, for CRS zone-guessing when no .prj sidecar exists to tell us what
        /// those coordinates are in.
        /// </summary>
        private static NetTopologySuite.Geometries.Envelope? TryReadShapefileExtent(string shapefilePath)
        {
            try
            {
                if (string.IsNullOrEmpty(shapefilePath) || !File.Exists(shapefilePath)) return null;

                var factory = new NetTopologySuite.Geometries.GeometryFactory();
                using var reader = new NetTopologySuite.IO.ShapefileDataReader(shapefilePath, factory);

                NetTopologySuite.Geometries.Envelope? extent = null;
                while (reader.Read())
                {
                    var geom = reader.Geometry;
                    if (geom == null || geom.IsEmpty) continue;

                    if (extent == null)
                        extent = geom.EnvelopeInternal.Copy();
                    else
                        extent.ExpandToInclude(geom.EnvelopeInternal);
                }

                return extent;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GeometryExportCoordinator: TryReadShapefileExtent('{shapefilePath}') failed — {ex.Message}");
                return null;
            }
        }
    }
}