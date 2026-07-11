using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Data;

public class DataTableExhibitOptions
{
    public string Title { get; set; } = string.Empty;
    public string NumericFormat { get; set; } = "F2";
    public Func<DataColumn, object, string?>? CellColor { get; set; }
    public string HeaderBackground { get; set; } = "#1F2937";
    public string HeaderTextColor { get; set; } = Colors.White;
    public string AltRowBackground { get; set; } = "#F3F4F6";
}

public static class DataTableExhibitBuilder
{
    public static void ComposeTable(IContainer container, DataTable data, DataTableExhibitOptions? options = null)
    {
        options ??= new DataTableExhibitOptions();

        container.Column(col =>
        {
            if (!string.IsNullOrEmpty(options.Title))
            {
                col.Item().PaddingBottom(4).Text(options.Title)
                   .Bold().FontSize(10).FontColor(Colors.Grey.Darken3);
            }

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    foreach (DataColumn _ in data.Columns)
                        c.RelativeColumn();
                });

                table.Header(header =>
                {
                    foreach (DataColumn column in data.Columns)
                    {
                        header.Cell().Background(options.HeaderBackground).Padding(4)
                              .Text(column.ColumnName.ToUpperInvariant())
                              .Bold().FontSize(8).FontColor(options.HeaderTextColor);
                    }
                });

                for (int rowIndex = 0; rowIndex < data.Rows.Count; rowIndex++)
                {
                    var row = data.Rows[rowIndex];
                    string rowBackground = rowIndex % 2 == 1 ? options.AltRowBackground : Colors.White;

                    foreach (DataColumn column in data.Columns)
                    {
                        var value = row[column];
                        var isNumeric = IsNumericType(column.DataType);
                        var text = FormatValue(value, isNumeric, options.NumericFormat);
                        var cellColor = options.CellColor?.Invoke(column, value);

                        var cell = table.Cell().Background(rowBackground).Padding(4);
                        var textStyle = cell.Text(text).FontSize(8);

                        if (isNumeric)
                            textStyle.AlignRight();

                        if (cellColor != null)
                            textStyle.FontColor(cellColor);
                        else
                            textStyle.FontColor(Colors.Grey.Darken3);
                    }
                }
            });
        });
    }

    // Default color rule for a WSElev-style delta column: red = drop, green = rise, gray = negligible.
    public static string? DeltaHeatmap(DataColumn column, object value)
        => DeltaHeatmap(column, value, "DELTA", 0.1);

    // Use when the delta column isn't named "DELTA" or you want a different neutral band.
    public static Func<DataColumn, object, string?> CreateDeltaHeatmap(string columnName, double neutralBand = 0.1)
        => (column, value) => DeltaHeatmap(column, value, columnName, neutralBand);

    private static string? DeltaHeatmap(DataColumn column, object value, string columnName, double neutralBand)
    {
        if (!column.ColumnName.Equals(columnName, StringComparison.OrdinalIgnoreCase))
            return null;
        if (value == null || value == DBNull.Value || !double.TryParse(value.ToString(), out var delta) || double.IsNaN(delta))
            return null;

        if (Math.Abs(delta) <= neutralBand)
            return Colors.Grey.Darken1;

        var intensity = Math.Min(Math.Abs(delta) / 5.0, 1.0); // clamp scale at 5 ft
        return delta < 0
            ? Interpolate(Colors.Red.Lighten2, Colors.Red.Darken3, intensity)
            : Interpolate(Colors.Green.Lighten2, Colors.Green.Darken3, intensity);
    }

    private static string FormatValue(object value, bool isNumeric, string numericFormat)
    {
        if (value == null || value == DBNull.Value)
            return string.Empty;
        if (isNumeric && double.TryParse(value.ToString(), out var d))
            return double.IsNaN(d) ? "—" : d.ToString(numericFormat);
        return value.ToString() ?? string.Empty;
    }

    private static bool IsNumericType(Type type)
    {
        return type == typeof(double) || type == typeof(float) || type == typeof(decimal) ||
               type == typeof(int) || type == typeof(long) || type == typeof(short);
    }

    private static string Interpolate(string hexLight, string hexDark, double t)
    {
        var (r1, g1, b1) = ToRgb(hexLight);
        var (r2, g2, b2) = ToRgb(hexDark);
        var r = (int)(r1 + (r2 - r1) * t);
        var g = (int)(g1 + (g2 - g1) * t);
        var b = (int)(b1 + (b2 - b1) * t);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private static (int, int, int) ToRgb(string hex)
    {
        hex = hex.TrimStart('#');
        return (Convert.ToInt32(hex.Substring(0, 2), 16),
                Convert.ToInt32(hex.Substring(2, 2), 16),
                Convert.ToInt32(hex.Substring(4, 2), 16));
    }
}