using System.Text;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AttendX.Services;

public interface IExportService
{
    byte[] ToCsv(ReportTable t);
    byte[] ToExcel(ReportTable t);
    byte[] ToPdf(ReportTable t);
}

/// <summary>CSV (plain), Excel (ClosedXML) and PDF (QuestPDF, Community licence) exports of any ReportTable.</summary>
public class ExportService : IExportService
{
    public byte[] ToCsv(ReportTable t)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CsvParser.Escape("AttendX - " + t.Title));
        sb.AppendLine(string.Join(",", t.Columns.Select(CsvParser.Escape)));
        foreach (var r in t.Rows) sb.AppendLine(string.Join(",", r.Select(CsvParser.Escape)));
        if (t.Footer != null) sb.AppendLine(string.Join(",", t.Footer.Select(CsvParser.Escape)));
        return [.. new UTF8Encoding(true).GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];   // BOM so Excel reads UTF-8
    }

    public byte[] ToExcel(ReportTable t)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Report");
        ws.Cell(1, 1).Value = "AttendX - " + t.Title;
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Cell(2, 1).Value = t.Subtitle;
        int hr = 4;
        for (int c = 0; c < t.Columns.Count; c++)
        {
            var cell = ws.Cell(hr, c + 1);
            cell.Value = t.Columns[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#14305A");
            cell.Style.Font.FontColor = XLColor.White;
        }
        int r = hr + 1;
        foreach (var row in t.Rows)
        {
            for (int c = 0; c < row.Length; c++)
            {
                var txt = row[c];
                if (decimal.TryParse(txt, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var num) && !txt.StartsWith("0") || txt == "0")
                    ws.Cell(r, c + 1).Value = num;
                else ws.Cell(r, c + 1).Value = txt;
            }
            r++;
        }
        if (t.Footer != null)
        {
            for (int c = 0; c < t.Footer.Length; c++) { ws.Cell(r, c + 1).Value = t.Footer[c]; ws.Cell(r, c + 1).Style.Font.Bold = true; }
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public byte[] ToPdf(ReportTable t)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(24);
                page.DefaultTextStyle(x => x.FontSize(8));
                page.Header().Column(col =>
                {
                    col.Item().Text("Attendance Management System").FontSize(10).SemiBold().FontColor(Colors.Blue.Darken4);
                    col.Item().Text(t.Title).FontSize(15).Bold();
                    col.Item().PaddingBottom(6).Text(t.Subtitle).FontSize(8).FontColor(Colors.Grey.Darken1);
                });
                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(cd => { foreach (var _ in t.Columns) cd.RelativeColumn(); });
                    table.Header(h =>
                    {
                        foreach (var c in t.Columns)
                            h.Cell().Background(Colors.Blue.Darken4).Padding(3).Text(c).FontColor(Colors.White).SemiBold();
                    });
                    foreach (var row in t.Rows)
                        foreach (var v in row)
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3).Text(v);
                    if (t.Footer != null)
                        foreach (var v in t.Footer)
                            table.Cell().Background(Colors.Grey.Lighten3).Padding(3).Text(v).Bold();
                });
                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("AttendX - page ");
                    x.CurrentPageNumber();
                    x.Span(" of ");
                    x.TotalPages();
                });
            });
        });
        return doc.GeneratePdf();
    }
}
