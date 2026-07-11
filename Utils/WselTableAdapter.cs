using HydroExplorer.View;
using System.Collections.Generic;
using System.Data;

namespace HydroExplorer.Reports
{
    public static class WselTableAdapter
    {
        public static DataTable ToDataTable(IEnumerable<WSELTableOxy> rows)
        {
            var table = new DataTable();
            table.Columns.Add("Reach", typeof(string));
            table.Columns.Add("River Sta", typeof(string));
            table.Columns.Add("Profile", typeof(string));
            table.Columns.Add("QTotal A", typeof(float));
            table.Columns.Add("WSElev A", typeof(double));
            table.Columns.Add("QTotal B", typeof(float));
            table.Columns.Add("WSElev B", typeof(double));
            table.Columns.Add("DELTA", typeof(double));

            foreach (var row in rows)
            {
                table.Rows.Add(
                    row.Reach,
                    row.RiverSta,
                    row.Profile,
                    row.QTotalA,
                    row.WSElevA,
                    row.QTotalB,
                    row.WSElevB,
                    row.DELTA);
            }

            return table;
        }
    }
}
