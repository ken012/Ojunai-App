using BizPilot.API.Common;
using BizPilot.API.Data;
using BizPilot.API.Models;
using BizPilot.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BizPilot.API.Services;

public class PdfExportService : IPdfExportService
{
    private readonly AppDbContext _db;

    public PdfExportService(AppDbContext db) => _db = db;

    public async Task<byte[]> GenerateReportPdfAsync(Guid businessId, string reportType, DateOnly from, DateOnly to)
    {
        var business = await _db.Businesses.FindAsync(businessId)
            ?? throw new KeyNotFoundException("Business not found.");
        var cs = BillingConfig.Symbol(business.Currency);

        return reportType switch
        {
            "sales" => await GenerateSalesReportAsync(business, cs, from, to),
            "expenses" => await GenerateExpensesReportAsync(business, cs, from, to),
            "monthly-pnl" => await GeneratePnlReportAsync(business, cs, from, to),
            _ => throw new ArgumentException($"Unknown report type: {reportType}")
        };
    }

    private async Task<byte[]> GenerateSalesReportAsync(Business biz, string cs, DateOnly from, DateOnly to)
    {
        var fromDt = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toDt = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var sales = await _db.Sales
            .Include(s => s.Contact)
            .Include(s => s.Items).ThenInclude(i => i.Product)
            .Where(s => s.BusinessId == biz.Id && s.CreatedAtUtc >= fromDt && s.CreatedAtUtc <= toDt)
            .OrderByDescending(s => s.CreatedAtUtc)
            .ToListAsync();

        var total = sales.Sum(s => s.TotalAmount);

        return BuildPdf(biz.Name, "Sales Report", from, to, doc =>
        {
            doc.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(1.2f); // Date
                    c.RelativeColumn(2.5f); // Items
                    c.RelativeColumn(1.5f); // Customer
                    c.RelativeColumn(1f);   // Status
                    c.RelativeColumn(1f);   // Method
                    c.RelativeColumn(1.2f); // Amount
                });

                table.Header(h =>
                {
                    foreach (var hdr in new[] { "Date", "Items", "Customer", "Status", "Method", "Amount" })
                        h.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text(hdr).Bold().FontSize(8);
                });

                foreach (var s in sales)
                {
                    var items = string.Join(", ", s.Items.Select(i => $"{i.Quantity:0.##} {i.Product.Unit} {i.Product.Name}"));
                    DataCell(table, s.CreatedAtUtc.ToString("dd MMM yyyy"));
                    DataCell(table, items);
                    DataCell(table, s.Contact?.Name ?? "—");
                    DataCell(table, s.PaymentStatus.ToString());
                    DataCell(table, s.PaymentMethod ?? "—");
                    DataCell(table, $"{cs}{s.TotalAmount:N0}", true);
                }
            });

            doc.Item().PaddingTop(10).AlignRight().Text($"Total: {cs}{total:N0}").Bold().FontSize(11);
            doc.Item().Text($"{sales.Count} sales").FontSize(8).FontColor(Colors.Grey.Medium);
        });
    }

    private async Task<byte[]> GenerateExpensesReportAsync(Business biz, string cs, DateOnly from, DateOnly to)
    {
        var fromDt = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toDt = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var expenses = await _db.Expenses
            .Where(e => e.BusinessId == biz.Id && e.CreatedAtUtc >= fromDt && e.CreatedAtUtc <= toDt)
            .OrderByDescending(e => e.CreatedAtUtc)
            .ToListAsync();

        var total = expenses.Sum(e => e.Amount);

        return BuildPdf(biz.Name, "Expenses Report", from, to, doc =>
        {
            doc.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(1.2f); // Date
                    c.RelativeColumn(1.5f); // Category
                    c.RelativeColumn(1.5f); // Paid To
                    c.RelativeColumn(2f);   // Notes
                    c.RelativeColumn(1f);   // Method
                    c.RelativeColumn(1.2f); // Amount
                });

                table.Header(h =>
                {
                    foreach (var hdr in new[] { "Date", "Category", "Paid To", "Notes", "Method", "Amount" })
                        h.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text(hdr).Bold().FontSize(8);
                });

                foreach (var e in expenses)
                {
                    DataCell(table, e.CreatedAtUtc.ToString("dd MMM yyyy"));
                    DataCell(table, e.Category);
                    DataCell(table, e.PaidTo ?? "—");
                    DataCell(table, e.Notes ?? "—");
                    DataCell(table, e.PaymentMethod ?? "—");
                    DataCell(table, $"{cs}{e.Amount:N0}", true);
                }
            });

            doc.Item().PaddingTop(10).AlignRight().Text($"Total: {cs}{total:N0}").Bold().FontSize(11);
            doc.Item().Text($"{expenses.Count} expenses").FontSize(8).FontColor(Colors.Grey.Medium);
        });
    }

    private async Task<byte[]> GeneratePnlReportAsync(Business biz, string cs, DateOnly from, DateOnly to)
    {
        var fromDt = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toDt = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var revenue = await _db.Sales
            .Where(s => s.BusinessId == biz.Id && s.CreatedAtUtc >= fromDt && s.CreatedAtUtc <= toDt)
            .SumAsync(s => s.TotalAmount);

        var allExpenses = await _db.Expenses
            .Where(e => e.BusinessId == biz.Id && e.CreatedAtUtc >= fromDt && e.CreatedAtUtc <= toDt)
            .ToListAsync();

        var cogs = allExpenses.Where(e => e.ExpenseType == "cogs").Sum(e => e.Amount);
        var operating = allExpenses.Where(e => e.ExpenseType != "cogs").Sum(e => e.Amount);
        var grossProfit = revenue - cogs;
        var netProfit = grossProfit - operating;
        var grossMargin = revenue > 0 ? grossProfit / revenue * 100 : 0;
        var netMargin = revenue > 0 ? netProfit / revenue * 100 : 0;

        return BuildPdf(biz.Name, "Profit & Loss Statement", from, to, doc =>
        {
            doc.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(3f);
                    c.RelativeColumn(2f);
                });

                PnlRow(table, "Revenue", $"{cs}{revenue:N0}", Colors.Green.Darken1);
                PnlRow(table, "Cost of Goods Sold", $"({cs}{cogs:N0})", Colors.Red.Darken1);
                PnlRow(table, "Gross Profit", $"{cs}{grossProfit:N0}", grossProfit >= 0 ? Colors.Green.Darken1 : Colors.Red.Darken1, true);
                PnlRow(table, "Operating Expenses", $"({cs}{operating:N0})", Colors.Red.Darken1);
                PnlRow(table, "Net Profit", $"{cs}{netProfit:N0}", netProfit >= 0 ? Colors.Green.Darken1 : Colors.Red.Darken1, true);
            });

            doc.Item().PaddingTop(15).Text($"Gross Margin: {grossMargin:F1}%  |  Net Margin: {netMargin:F1}%")
                .FontSize(9).FontColor(Colors.Grey.Darken1);
        });
    }

    private static byte[] BuildPdf(string businessName, string title, DateOnly from, DateOnly to, Action<ColumnDescriptor> content)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginHorizontal(40);
                page.MarginVertical(30);

                page.Header().Column(col =>
                {
                    col.Item().Text(businessName).Bold().FontSize(16);
                    col.Item().Text(title).FontSize(13).FontColor(Colors.Grey.Darken1);
                    col.Item().Text($"{from:dd MMM yyyy} — {to:dd MMM yyyy}").FontSize(9).FontColor(Colors.Grey.Medium);
                    col.Item().PaddingBottom(12).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                });

                page.Content().Column(content);

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Generated by BizPilot  •  ").FontSize(7).FontColor(Colors.Grey.Medium);
                    t.Span(DateTime.UtcNow.ToString("dd MMM yyyy HH:mm UTC")).FontSize(7).FontColor(Colors.Grey.Medium);
                });
            });
        });

        return document.GeneratePdf();
    }

    private static void DataCell(TableDescriptor table, string text, bool alignRight = false)
    {
        var cell = table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(4);
        if (alignRight) cell = cell.AlignRight();
        cell.Text(text).FontSize(8);
    }

    private static void PnlRow(TableDescriptor table, string label, string value, string color, bool bold = false)
    {
        var labelCell = table.Cell().Padding(8).BorderBottom(1).BorderColor(Colors.Grey.Lighten3);
        var valueCell = table.Cell().Padding(8).BorderBottom(1).BorderColor(Colors.Grey.Lighten3).AlignRight();

        if (bold)
        {
            labelCell.Text(label).Bold().FontSize(10);
            valueCell.Text(value).Bold().FontSize(10).FontColor(color);
        }
        else
        {
            labelCell.Text(label).FontSize(10);
            valueCell.Text(value).FontSize(10).FontColor(color);
        }
    }
}
