using OxyPlot;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.IO;


public static class HydroReportGenerator
{
    public static void GenerateHydrographReport(
        PlotModel hydrographPlot,
        PlotModel hyetographPlot,
        string outputPath,
        string projectName = "HydroExplorer",
        double rainfallTotal = 0,
        double lossTotal = 0,
        double rainfallExcessTotal = 0,
        double initialLoss = 0,
        double infiltrationIndex = 0)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        // Export plots to PNG bytes
        var hydrographBytes = ExportPlot(hydrographPlot, 800, 350);
        var hyetographBytes = ExportPlot(hyetographPlot, 800, 200);

        QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter.Landscape());
                page.Margin(1, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));

                // Header
                page.Header().Column(header =>
                {
                    header.Item().Row(row =>
                    {
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text(projectName)
                               .Bold().FontSize(14);
                            col.Item().Text("OUTFALL HYDROGRAPH")
                               .FontSize(11).FontColor(Colors.Grey.Darken2);
                        });
                        row.ConstantItem(200).AlignRight().Column(col =>
                        {
                            col.Item().Text($"Date: {DateTime.Now:MM/dd/yyyy}")
                               .FontSize(9).FontColor(Colors.Grey.Medium);
                            col.Item().Text("HydroExplorer")
                               .FontSize(9).FontColor(Colors.Grey.Medium);
                        });
                    });
                    header.Item().PaddingTop(4).LineHorizontal(0.5f)
                          .LineColor(Colors.Grey.Medium);
                });

                // Content
                page.Content().PaddingTop(8).Column(col =>
                {
                    // Stats row
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Border(0.5f).BorderColor(Colors.Grey.Lighten1)
                           .Padding(6).Column(stats =>
                           {
                               stats.Item().Text("HYETOGRAPH (INCHES)")
                                    .Bold().FontSize(9).Underline();
                               stats.Item().PaddingTop(4).Table(table =>
                               {
                                   table.ColumnsDefinition(c =>
                                   {
                                       c.RelativeColumn();
                                       c.ConstantColumn(60);
                                   });

                                   void Row(string label, double value)
                                   {
                                       table.Cell().Text(label).FontSize(8)
                                            .FontColor(Colors.Grey.Darken1);
                                       table.Cell().AlignRight()
                                            .Text($"{value:F2}").FontSize(8);
                                   }

                                   Row("RAINFALL TOTAL", rainfallTotal);
                                   Row("LOSS TOTAL", lossTotal);
                                   Row("RAINFALL - EXCESS TOTAL", rainfallExcessTotal);
                                   Row("INITIAL LOSS", initialLoss);
                                   Row("INFILTRATION INDEX", infiltrationIndex);
                               });
                           });

                        row.ConstantItem(8);

                        // Hyetograph
                        row.RelativeItem(3).Column(hyet =>
                        {
                            hyet.Item().Text("HYETOGRAPH").Bold().FontSize(8)
                                .FontColor(Colors.Grey.Darken2);
                            hyet.Item().PaddingTop(2).Image(hyetographBytes);
                        });
                    });

                    col.Item().PaddingTop(8);

                    // Hydrograph
                    col.Item().Column(hydro =>
                    {
                        hydro.Item().Text("HYDROGRAPH").Bold().FontSize(8)
                             .FontColor(Colors.Grey.Darken2);
                        hydro.Item().PaddingTop(2).Image(hydrographBytes);
                    });
                });

                // Footer
                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Page ").FontSize(8).FontColor(Colors.Grey.Medium);
                    x.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
                    x.Span(" of ").FontSize(8).FontColor(Colors.Grey.Medium);
                    x.TotalPages().FontSize(8).FontColor(Colors.Grey.Medium);
                });
            });
        })
        .GeneratePdf(outputPath);

        System.Diagnostics.Debug.WriteLine($"Report saved to {outputPath}");
    }

    private static byte[] ExportPlot(PlotModel model, int width, int height)
    {
        var exporter = new OxyPlot.SkiaSharp.PngExporter { Width = width, Height = height };
        using var stream = new MemoryStream();
        exporter.Export(model, stream);
        return stream.ToArray();
    }
}