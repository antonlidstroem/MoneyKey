// ════════════════════════════════════════════════════════════════════════════
// BudgetPlanner.Core/Services/ExportService.cs
//
// PHASE 2 FIXES:
//   1. ExportToExcel: project columns were swapped (Name↔BudgetAmount). Fixed.
//   2. ExportToExcel: MonthlySummary sheet excluded one-time transactions.
//      Filter `Recurrence != OneTime` removed — all transactions are included.
//
// This class lives in the same namespace as the rest of Core/Services.cs.
// Remove the ExportService class from Services.cs and add a reference to
// this file instead (or simply place this file alongside Services.cs —
// the C# compiler will merge both into the same assembly).
// ════════════════════════════════════════════════════════════════════════════
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using BudgetPlanner.Core.DTOs;

namespace BudgetPlanner.Core.Services;

public class ExportService
{
    static ExportService() => QuestPDF.Settings.License = LicenseType.Community;

    public byte[] ExportToPdf(List<TransactionDto> transactions, string budgetName)
    {
        static IContainer H(IContainer c) => c.Background("#263238").Padding(4);
        IContainer D(IContainer c, int i) => c.Background(i % 2 == 0 ? "#FFFFFF" : "#F5F5F5").Padding(4);

        return Document.Create(cont =>
        {
            cont.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape()); page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));

                page.Header().Column(col =>
                {
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Text(budgetName).Bold().FontSize(16).FontColor("#1565C0");
                        r.ConstantItem(200).AlignRight()
                            .Text($"Exporterat {DateTime.Now:yyyy-MM-dd HH:mm}").FontSize(8).FontColor("#78909C");
                    });
                    col.Item().PaddingTop(4).LineHorizontal(1).LineColor("#E0E0E0");
                });

                page.Content().PaddingTop(8).Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.ConstantColumn(70); cols.RelativeColumn(3); cols.RelativeColumn(2);
                        cols.ConstantColumn(80); cols.ConstantColumn(70); cols.ConstantColumn(80);
                    });
                    table.Header(h =>
                    {
                        foreach (var t in new[] { "Datum", "Beskrivning", "Kategori", "Återkommande", "Brutto", "Belopp" })
                            h.Cell().Element(H).Text(t).Bold().FontColor("#FFFFFF");
                    });
                    for (var i = 0; i < transactions.Count; i++)
                    {
                        var tx = transactions[i]; var idx = i;
                        IContainer C(IContainer c) => D(c, idx);
                        table.Cell().Element(C).Text(tx.StartDate.ToString("yyyy-MM-dd"));
                        table.Cell().Element(C).Text(tx.Description ?? "");
                        table.Cell().Element(C).Text(tx.CategoryName);
                        table.Cell().Element(C).Text(tx.Recurrence.ToString());
                        table.Cell().Element(C).AlignRight().Text(tx.GrossAmount?.ToString("N2") ?? "");
                        table.Cell().Element(C).AlignRight()
                            .Text(tx.NetAmount.ToString("N2"))
                            .FontColor(tx.NetAmount >= 0 ? "#2E7D32" : "#C62828");
                    }
                });

                page.Footer().Row(r =>
                {
                    var inc = transactions.Where(t => t.NetAmount > 0).Sum(t => t.NetAmount);
                    var exp = transactions.Where(t => t.NetAmount < 0).Sum(t => t.NetAmount);
                    r.RelativeItem()
                        .Text($"{transactions.Count} poster | Inkomster: {inc:N2} kr | Utgifter: {exp:N2} kr | Netto: {inc + exp:N2} kr")
                        .FontSize(8);
                    r.ConstantItem(60).AlignRight().DefaultTextStyle(x => x.FontSize(8))
                        .Text(x => { x.Span("Sida "); x.CurrentPageNumber(); x.Span(" av "); x.TotalPages(); });
                });
            });
        }).GeneratePdf();
    }

    public byte[] ExportToExcel(List<TransactionDto> transactions, List<ProjectDto> projects, string budgetName)
    {
        using var wb = new XLWorkbook();

        // ── Sheet 1: Transactions ─────────────────────────────────────────────
        var ws = wb.AddWorksheet("Transaktioner");
        var hdrs = new[] { "Datum", "Slut", "Beskrivning", "Kategori", "Typ", "Återkommande", "Månad", "%", "Brutto", "Belopp", "Projekt", "Kontering" };
        for (var i = 0; i < hdrs.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = hdrs[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#263238");
            cell.Style.Font.FontColor = XLColor.White;
        }
        for (var i = 0; i < transactions.Count; i++)
        {
            var tx = transactions[i]; var row = i + 2;
            ws.Cell(row, 1).Value  = tx.StartDate.ToString("yyyy-MM-dd");
            ws.Cell(row, 2).Value  = tx.EndDate?.ToString("yyyy-MM-dd") ?? "";
            ws.Cell(row, 3).Value  = tx.Description ?? "";
            ws.Cell(row, 4).Value  = tx.CategoryName;
            ws.Cell(row, 5).Value  = tx.Type.ToString();
            ws.Cell(row, 6).Value  = tx.Recurrence.ToString();
            ws.Cell(row, 7).Value  = tx.Month?.ToString() ?? "";
            ws.Cell(row, 8).Value  = (double?)tx.Rate ?? 0;
            ws.Cell(row, 9).Value  = (double?)tx.GrossAmount ?? 0;
            ws.Cell(row, 10).Value = (double)tx.NetAmount;
            ws.Cell(row, 11).Value = tx.ProjectName ?? "";
            ws.Cell(row, 12).Value = tx.HasKontering ? "Ja" : "";
            ws.Cell(row, 10).Style.Font.FontColor =
                tx.NetAmount >= 0 ? XLColor.FromHtml("#2E7D32") : XLColor.FromHtml("#C62828");
        }
        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(1);

        // ── Sheet 2: Monthly summary ──────────────────────────────────────────
        // FIX: removed Recurrence != OneTime filter — all transactions are included
        var ws2 = wb.AddWorksheet("Månadssammanfattning");
        ws2.Cell(1, 1).Value = "Månad";
        ws2.Cell(1, 2).Value = "Inkomster";
        ws2.Cell(1, 3).Value = "Utgifter";
        ws2.Cell(1, 4).Value = "Netto";
        ws2.Row(1).Style.Font.Bold = true;
        ws2.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1976D2");
        ws2.Row(1).Style.Font.FontColor = XLColor.White;

        // All transactions grouped by year-month (no recurrence filter)
        var monthly = transactions
            .GroupBy(t => t.StartDate.ToString("yyyy-MM"))
            .OrderBy(g => g.Key);

        var rw2 = 2;
        foreach (var g in monthly)
        {
            ws2.Cell(rw2, 1).Value = g.Key;
            ws2.Cell(rw2, 2).Value = (double)g.Where(t => t.NetAmount > 0).Sum(t => t.NetAmount);
            ws2.Cell(rw2, 3).Value = (double)g.Where(t => t.NetAmount < 0).Sum(t => t.NetAmount);
            ws2.Cell(rw2, 4).Value = (double)g.Sum(t => t.NetAmount);
            rw2++;
        }
        ws2.Columns().AdjustToContents();

        // ── Sheet 3: Projects ─────────────────────────────────────────────────
        var ws3 = wb.AddWorksheet("Projekt");
        // FIX: columns were swapped — BudgetAmount in col1, Name in col2. Corrected:
        ws3.Cell(1, 1).Value = "Projekt";       // Name   → col 1
        ws3.Cell(1, 2).Value = "Budget (kr)";   // Budget → col 2
        ws3.Cell(1, 3).Value = "Spenderat (kr)";
        ws3.Cell(1, 4).Value = "Återstår (kr)";
        ws3.Cell(1, 5).Value = "Progress %";
        ws3.Row(1).Style.Font.Bold = true;
        ws3.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1976D2");
        ws3.Row(1).Style.Font.FontColor = XLColor.White;

        for (var i = 0; i < projects.Count; i++)
        {
            var p = projects[i];
            ws3.Cell(i + 2, 1).Value = p.Name;                      // FIX: Name in col 1
            ws3.Cell(i + 2, 2).Value = (double)p.BudgetAmount;      // FIX: Budget in col 2
            ws3.Cell(i + 2, 3).Value = (double)p.SpentAmount;
            ws3.Cell(i + 2, 4).Value = (double)p.RemainingAmount;
            ws3.Cell(i + 2, 5).Value = p.ProgressPercent;

            // Highlight over-budget rows in red
            if (p.SpentAmount < 0 && Math.Abs(p.SpentAmount) > p.BudgetAmount)
            {
                ws3.Cell(i + 2, 3).Style.Font.FontColor = XLColor.FromHtml("#C62828");
                ws3.Cell(i + 2, 4).Style.Font.FontColor = XLColor.FromHtml("#C62828");
            }
        }
        ws3.Columns().AdjustToContents();

        // ── Sheet 4: Kontering ────────────────────────────────────────────────
        var ws4 = wb.AddWorksheet("Kontering");
        ws4.Cell(1, 1).Value = "Transaktion";
        ws4.Cell(1, 2).Value = "Konto Nr";
        ws4.Cell(1, 3).Value = "Kostnadsst.";
        ws4.Cell(1, 4).Value = "Belopp";
        ws4.Cell(1, 5).Value = "%";
        ws4.Cell(1, 6).Value = "Beskrivning";
        ws4.Row(1).Style.Font.Bold = true;
        ws4.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1976D2");
        ws4.Row(1).Style.Font.FontColor = XLColor.White;

        var kr = 2;
        foreach (var tx in transactions.Where(t => t.HasKontering))
            foreach (var k in tx.KonteringRows)
            {
                ws4.Cell(kr, 1).Value = $"{tx.StartDate:yyyy-MM-dd} {tx.Description}";
                ws4.Cell(kr, 2).Value = k.KontoNr;
                ws4.Cell(kr, 3).Value = k.CostCenter ?? "";
                ws4.Cell(kr, 4).Value = (double)k.Amount;
                ws4.Cell(kr, 5).Value = (double?)k.Percentage ?? 0;
                ws4.Cell(kr, 6).Value = k.Description ?? "";
                kr++;
            }
        ws4.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
