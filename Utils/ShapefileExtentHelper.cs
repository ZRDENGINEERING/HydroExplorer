using Mapsui;
using Mapsui.Projections;
using System.Collections.Generic;
using System.Linq;

namespace HydroExplorer.Reports
{
    public static class ShapefileExtentHelper
    {
        public static MRect? GetExtentForStations(string xsShpPath, IEnumerable<string> riverStas, double padFactor = 0.15)
        {
            var staSet = riverStas
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (staSet.Count == 0) return null;

            using var reader = new NetTopologySuite.IO.ShapefileDataReader(
                xsShpPath, NetTopologySuite.Geometries.GeometryFactory.Default);

            var header = reader.DbaseHeader;
            int staIdx = -1;
            for (int i = 0; i < header.Fields.Length; i++)
            {
                if (header.Fields[i].Name.Contains("RS", StringComparison.OrdinalIgnoreCase))
                { staIdx = i + 1; break; }
            }
            if (staIdx == -1) return null;

            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            bool found = false;

            while (reader.Read())
            {
                var rs = reader.GetString(staIdx)?.Trim();
                if (rs == null || !staSet.Contains(rs)) continue;

                foreach (var c in reader.Geometry.Coordinates)
                {
                    var (px, py) = SphericalMercator.FromLonLat(c.X, c.Y);
                    if (px < minX) minX = px;
                    if (px > maxX) maxX = px;
                    if (py < minY) minY = py;
                    if (py > maxY) maxY = py;
                    found = true;
                }
            }

            if (!found) return null;

            double dx = (maxX - minX) * padFactor;
            double dy = (maxY - minY) * padFactor;
            return new MRect(minX - dx, minY - dy, maxX + dx, maxY + dy);
        }
    }
}
