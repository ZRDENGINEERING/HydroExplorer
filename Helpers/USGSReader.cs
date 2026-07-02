using HydroExplorer.Helpers;
using Mapsui.Nts.Providers.Shapefile;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HydroExplorer.Utils
{
    public record GageResult(
        string SiteNo,
        string SiteName,
        double Lat,
        double Lon,
        double DistanceMiles,
        string HucCode,
        double? DrainageAreaSqMi);

    public enum GageLookupStatus
    {
        Success,
        NoCentroid,
        NoGageInRange,
        NoProjectSettings,
        Failed
    }

    public static class USGSReader
    {
        private const double SearchRadiusMiles = 25;

        private static readonly HttpClient _client = new()
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        /// <summary>
        /// Finds the nearest USGS stream gage to a project's boundary centroid.
        /// Gage identity/location is static once resolved, so this fetches at most
        /// once ever per project — if ProjectSettings already has a saved gage,
        /// that's returned directly with no network call.
        /// </summary>
        public static async Task<(GageResult? Result, GageLookupStatus Status)> GetNearestGageWithStatusAsync(string projPath)
        {
            if (string.IsNullOrEmpty(projPath)) return (null, GageLookupStatus.Failed);

            var settingsRepo = App.ServiceProvider.GetRequiredService<IUserSettingsRepo>();
            var settings = await settingsRepo.GetSettingsFresh();

            if (!settings.Projects.TryGetValue(projPath, out var projSettings))
            {
                System.Diagnostics.Debug.WriteLine($"USGSReader: no project settings found for '{projPath}'.");
                return (null, GageLookupStatus.NoProjectSettings);
            }

            // ── Already resolved for this project — never fetch again ─────────
            if (!string.IsNullOrEmpty(projSettings.GageSiteNo))
            {
                var cached = new GageResult(
                    projSettings.GageSiteNo,
                    projSettings.GageName,
                    projSettings.GageLat,
                    projSettings.GageLon,
                    projSettings.GageDistanceMiles,
                    projSettings.GageHucCode,
                    projSettings.GageDrainageAreaSqMi);
                return (cached, GageLookupStatus.Success);
            }

            // ── Get project centroid from BNDY.shp ───────────────────────────
            var centroid = GetProjectCentroid(projSettings);
            if (centroid is null)
            {
                //System.Diagnostics.Debug.WriteLine("USGSReader: could not determine project centroid (BNDY.shp missing or unreadable).");
                return (null, GageLookupStatus.NoCentroid);
            }

            var (lon, lat) = centroid.Value;

            // ── Find candidate sites within bounding box ─────────────────────
            var result = await FindNearestSiteAsync(lat, lon);
            if (result is null)
            {
                System.Diagnostics.Debug.WriteLine($"USGSReader: no stream gage found within {SearchRadiusMiles} mi.");
                return (null, GageLookupStatus.NoGageInRange);
            }

            // ── Cache to ProjectSettings ──────────────────────────────────────
            projSettings.GageSiteNo = result.SiteNo;
            projSettings.GageName = result.SiteName;
            projSettings.GageLat = result.Lat;
            projSettings.GageLon = result.Lon;
            projSettings.GageDistanceMiles = result.DistanceMiles;
            projSettings.GageHucCode = result.HucCode;
            projSettings.GageDrainageAreaSqMi = result.DrainageAreaSqMi;

            await settingsRepo.SaveSettings(settings);

            EventBus.PublishGageDataReady(result);

            return (result, GageLookupStatus.Success);
        }

        /// <summary>
        /// Convenience overload for callers that don't need the lookup status.
        /// </summary>
        public static async Task<GageResult?> GetNearestGageAsync(string projPath)
        {
            var (result, _) = await GetNearestGageWithStatusAsync(projPath);
            return result;
        }

        /// <summary>
        /// Reads RIVER.shp for a project and returns its extent centroid as (lon, lat).
        /// Uses the path already resolved and cached by MapOverView.BuildPaths in
        /// ProjectSettings.SpatialRiverPath — avoids re-deriving the Spatial directory
        /// independently, which previously caused a mismatch for deeply nested projects
        /// where ProjRoot sits several directories above the actual project file.
        /// </summary>
        private static (double lon, double lat)? GetProjectCentroid(ProjectSettings projSettings)
        {
            string pathRiver = projSettings.SpatialRiverPath;

            if (string.IsNullOrEmpty(pathRiver) || !File.Exists(pathRiver))
            {
                //System.Diagnostics.Debug.WriteLine($"USGSReader: RIVER.shp not found at '{pathRiver}'.");
                return null;
            }

            try
            {
                var shp = new ShapeFile(pathRiver, true);
                if (shp.GetExtent() is not { } extent) return null;

                double lon = (extent.MaxX + extent.MinX) / 2;
                double lat = (extent.MaxY + extent.MinY) / 2;

                return (lon, lat);
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"USGSReader: failed to read RIVER.shp centroid — {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Queries the NWIS site service for stream gages within a bounding box
        /// around (lat, lon), and returns the nearest one within SearchRadiusMiles.
        /// </summary>
        private static async Task<GageResult?> FindNearestSiteAsync(double lat, double lon)
        {
            // ~1 degree latitude ≈ 69 mi; longitude varies with latitude, approximate is fine for a 25 mi box
            double latDelta = SearchRadiusMiles / 69.0;
            double lonDelta = SearchRadiusMiles / (69.0 * Math.Cos(lat * Math.PI / 180.0));

            double west = lon - lonDelta;
            double east = lon + lonDelta;
            double south = lat - latDelta;
            double north = lat + latDelta;

            string bBox = $"{west:F6},{south:F6},{east:F6},{north:F6}";

            string url = "https://waterservices.usgs.gov/nwis/site/"
                + $"?format=rdb&bBox={bBox}&siteType=ST&siteStatus=active&siteOutput=expanded";

            try
            {
                using var response = await _client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"USGSReader: site query failed with status {response.StatusCode}.");
                    return null;
                }

                string json = await response.Content.ReadAsStringAsync();
                var sites = ParseSiteServiceRdb(json, lat, lon);

                return sites.OrderBy(s => s.DistanceMiles)
                             .FirstOrDefault(s => s.DistanceMiles <= SearchRadiusMiles);
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"USGSReader: site query exception — {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// The NWIS site service's JSON format is a sparse, loosely structured payload;
        /// the RDB (tab-delimited) format is far more reliable to parse for site lat/lon/name.
        /// We use the rdb override instead of json for this call.
        /// </summary>
        private static List<GageResult> ParseSiteServiceRdb(string content, double originLat, double originLon)
        {
            var results = new List<GageResult>();

            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            int siteNoIdx = -1, siteNameIdx = -1, latIdx = -1, lonIdx = -1, hucIdx = -1, drainAreaIdx = -1;
            bool headerParsed = false;

            foreach (var rawLine in lines)
            {
                string line = rawLine.TrimEnd('\r');
                if (line.StartsWith('#')) continue;

                var cols = line.Split('\t');

                if (!headerParsed)
                {
                    siteNoIdx = Array.IndexOf(cols, "site_no");
                    siteNameIdx = Array.IndexOf(cols, "station_nm");
                    latIdx = Array.IndexOf(cols, "dec_lat_va");
                    lonIdx = Array.IndexOf(cols, "dec_long_va");
                    hucIdx = Array.IndexOf(cols, "huc_cd");
                    drainAreaIdx = Array.IndexOf(cols, "drain_area_va");
                    headerParsed = true;
                    continue;
                }

                // Skip the RDB format-spec line (e.g. "5s\t15s\t...")
                if (cols.Length > 0 && cols[0].EndsWith('s')) continue;

                if (siteNoIdx < 0 || latIdx < 0 || lonIdx < 0) continue;
                if (cols.Length <= Math.Max(siteNoIdx, Math.Max(latIdx, lonIdx))) continue;

                if (!double.TryParse(cols[latIdx], out double siteLat)) continue;
                if (!double.TryParse(cols[lonIdx], out double siteLon)) continue;

                double distance = HaversineMiles(originLat, originLon, siteLat, siteLon);

                string hucCode = hucIdx >= 0 && cols.Length > hucIdx ? cols[hucIdx] : string.Empty;

                double? drainageAreaSqMi = null;
                if (drainAreaIdx >= 0 && cols.Length > drainAreaIdx &&
                    double.TryParse(cols[drainAreaIdx], out double drainArea))
                {
                    drainageAreaSqMi = drainArea;
                }

                results.Add(new GageResult(
                    SiteNo: cols[siteNoIdx],
                    SiteName: siteNameIdx >= 0 && cols.Length > siteNameIdx ? cols[siteNameIdx] : string.Empty,
                    Lat: siteLat,
                    Lon: siteLon,
                    DistanceMiles: Math.Round(distance, 2),
                    HucCode: hucCode,
                    DrainageAreaSqMi: drainageAreaSqMi));
            }

            return results;
        }

        private static double HaversineMiles(double lat1, double lon1, double lat2, double lon2)
        {
            const double earthRadiusMiles = 3958.8;

            double dLat = (lat2 - lat1) * Math.PI / 180.0;
            double dLon = (lon2 - lon1) * Math.PI / 180.0;

            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0)
                * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            return earthRadiusMiles * c;
        }
    }
}