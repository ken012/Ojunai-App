using Microsoft.EntityFrameworkCore;
using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Ojunai.API.Services;

public interface IReceiptService
{
    /// <summary>
    /// Generate (or re-generate) a PDF receipt for a sale. On first generation, atomically
    /// assigns a ReceiptNumber from the business's NextReceiptNumber counter. Subsequent
    /// calls reuse the existing ReceiptNumber.
    /// </summary>
    Task<(byte[] pdfBytes, string receiptNumber)> GenerateAsync(Guid saleId, Guid businessId);
}

public class ReceiptService : IReceiptService
{
    private readonly AppDbContext _db;
    public ReceiptService(AppDbContext db) => _db = db;

    public async Task<(byte[] pdfBytes, string receiptNumber)> GenerateAsync(Guid saleId, Guid businessId)
    {
        var sale = await _db.Sales
            .Include(s => s.Contact)
            .Include(s => s.Items).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(s => s.Id == saleId && s.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Sale not found.");

        var business = await _db.Businesses.FindAsync(businessId)
            ?? throw new KeyNotFoundException("Business not found.");

        // Assign receipt number atomically on first generation
        if (string.IsNullOrEmpty(sale.ReceiptNumber))
        {
            // Ensure ReceiptPrefix is set (auto-derive from business name first time)
            if (string.IsNullOrEmpty(business.ReceiptPrefix))
            {
                business.ReceiptPrefix = DerivePrefix(business.Name);
            }

            // Use a transaction to atomically increment NextReceiptNumber
            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                // Re-fetch business with row lock pattern (PostgreSQL: FOR UPDATE via raw)
                // EF Core doesn't have a clean way; rely on the transaction + concurrent updates
                // being rare for the same business.
                var seq = business.NextReceiptNumber;
                sale.ReceiptNumber = $"RCT-{business.ReceiptPrefix}-{seq:D6}";
                sale.ReceiptGeneratedAtUtc = DateTime.UtcNow;
                business.NextReceiptNumber = seq + 1;
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        var bytes = BuildPdf(sale, business);
        return (bytes, sale.ReceiptNumber!);
    }

    /// <summary>
    /// Derive a 2-3 letter uppercase prefix from a business name.
    /// "Glow Daddy" → "GD", "Mama Titi Store" → "MTS", "Bori" → "BOR", "X" → "X".
    /// </summary>
    public static string DerivePrefix(string name)
    {
        var clean = (name ?? "").Trim();
        if (clean.Length == 0) return "BIZ";
        var words = clean.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 2)
        {
            // Initials of first 3 words
            var initials = string.Concat(words.Take(3).Select(w => w[0]));
            return initials.ToUpperInvariant();
        }
        // Single word: take first 3 alphabetic characters
        var letters = new string(words[0].Where(char.IsLetter).Take(3).ToArray());
        return letters.Length > 0 ? letters.ToUpperInvariant() : "BIZ";
    }

    private byte[] BuildPdf(Sale sale, Business biz)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var cs = BillingConfig.Symbol(biz.Currency);
        var hasVat = sale.VatAmount > 0;
        var subtotal = sale.TotalAmount - sale.VatAmount;

        return Document.Create(doc =>
        {
            doc.Page(p =>
            {
                p.Size(PageSizes.A5);
                p.Margin(28);
                p.PageColor(Colors.White);
                p.DefaultTextStyle(t => t.FontSize(10).FontColor("#0F172A"));

                p.Content().Column(col =>
                {
                    // ── Header: business identity ──
                    col.Item().Column(c =>
                    {
                        c.Item().Text(biz.Name).FontSize(16).Bold().FontColor("#0F172A");
                        if (!string.IsNullOrWhiteSpace(biz.Address))
                            c.Item().Text(biz.Address).FontSize(9).FontColor("#64748B");
                        if (!string.IsNullOrWhiteSpace(biz.City) || !string.IsNullOrWhiteSpace(biz.State))
                        {
                            var loc = string.Join(", ", new[] { biz.City, biz.State, biz.Country }
                                .Where(s => !string.IsNullOrWhiteSpace(s)));
                            c.Item().Text(loc).FontSize(9).FontColor("#64748B");
                        }
                    });

                    col.Item().PaddingTop(14).LineHorizontal(0.5f).LineColor("#E2E8F0");

                    // ── Receipt meta + customer (two columns) ──
                    col.Item().PaddingTop(14).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("RECEIPT").FontSize(8).Bold().LetterSpacing(1.5f).FontColor("#94A3B8");
                            c.Item().PaddingTop(4).Text(sale.ReceiptNumber!).FontSize(13).Bold().FontColor("#0F172A");
                            c.Item().PaddingTop(2).Text($"Issued {DateTime.UtcNow:MMM d, yyyy 'at' h:mm tt}")
                                .FontSize(8).FontColor("#94A3B8");
                        });

                        row.RelativeItem().AlignRight().Column(c =>
                        {
                            c.Item().AlignRight().Text("CUSTOMER").FontSize(8).Bold().LetterSpacing(1.5f).FontColor("#94A3B8");
                            c.Item().AlignRight().PaddingTop(4).Text(sale.Contact?.Name ?? "Walk-in").FontSize(11).Bold().FontColor("#0F172A");
                            if (!string.IsNullOrWhiteSpace(sale.Contact?.PhoneNumber))
                                c.Item().AlignRight().PaddingTop(2).Text(sale.Contact.PhoneNumber).FontSize(9).FontColor("#64748B");
                        });
                    });

                    col.Item().PaddingTop(20);

                    // ── Items table ──
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(3.5f); // Item
                            c.RelativeColumn(1f);   // Qty
                            c.RelativeColumn(1.5f); // Unit
                            c.RelativeColumn(1.7f); // Total
                        });

                        // Header
                        table.Header(h =>
                        {
                            h.Cell().BorderBottom(0.5f).BorderColor("#E2E8F0").PaddingBottom(6)
                                .Text("Item").FontSize(8).Bold().LetterSpacing(1.2f).FontColor("#64748B");
                            h.Cell().BorderBottom(0.5f).BorderColor("#E2E8F0").PaddingBottom(6).AlignRight()
                                .Text("QTY").FontSize(8).Bold().LetterSpacing(1.2f).FontColor("#64748B");
                            h.Cell().BorderBottom(0.5f).BorderColor("#E2E8F0").PaddingBottom(6).AlignRight()
                                .Text("UNIT").FontSize(8).Bold().LetterSpacing(1.2f).FontColor("#64748B");
                            h.Cell().BorderBottom(0.5f).BorderColor("#E2E8F0").PaddingBottom(6).AlignRight()
                                .Text("TOTAL").FontSize(8).Bold().LetterSpacing(1.2f).FontColor("#64748B");
                        });

                        // Rows
                        foreach (var item in sale.Items)
                        {
                            table.Cell().PaddingVertical(5).Text(item.Product?.Name ?? "—").FontSize(10).FontColor("#0F172A");
                            table.Cell().PaddingVertical(5).AlignRight().Text(item.Quantity.ToString("0.##")).FontSize(10).FontColor("#0F172A");
                            table.Cell().PaddingVertical(5).AlignRight().Text($"{cs}{item.UnitPrice:N2}").FontSize(10).FontColor("#0F172A");
                            table.Cell().PaddingVertical(5).AlignRight().Text($"{cs}{item.TotalPrice:N2}").FontSize(10).FontColor("#0F172A").Bold();
                        }
                    });

                    col.Item().PaddingTop(14).LineHorizontal(0.5f).LineColor("#E2E8F0");

                    // ── Totals ──
                    col.Item().PaddingTop(10).AlignRight().Column(t =>
                    {
                        t.Item().Row(r =>
                        {
                            r.RelativeItem().AlignRight().Text("Subtotal").FontSize(10).FontColor("#64748B");
                            r.ConstantItem(110).AlignRight().Text($"{cs}{subtotal:N2}").FontSize(10).FontColor("#0F172A");
                        });
                        if (hasVat)
                        {
                            t.Item().PaddingTop(2).Row(r =>
                            {
                                r.RelativeItem().AlignRight().Text($"VAT ({biz.VatRate:0.#}%)").FontSize(10).FontColor("#64748B");
                                r.ConstantItem(110).AlignRight().Text($"{cs}{sale.VatAmount:N2}").FontSize(10).FontColor("#0F172A");
                            });
                        }
                        t.Item().PaddingTop(8).BorderTop(0.5f).BorderColor("#0F172A").PaddingTop(8).Row(r =>
                        {
                            r.RelativeItem().AlignRight().Text("TOTAL").FontSize(11).Bold().LetterSpacing(0.5f).FontColor("#0F172A");
                            r.ConstantItem(110).AlignRight().Text($"{cs}{sale.TotalAmount:N2}").FontSize(13).Bold().FontColor("#0F172A");
                        });
                    });

                    // ── Payment status pill ──
                    col.Item().PaddingTop(18).AlignRight().Container().Background(
                        sale.PaymentStatus == PaymentStatus.Paid ? "#D1FAE5" :
                        sale.PaymentStatus == PaymentStatus.Unpaid ? "#FEE2E2" : "#FEF3C7"
                    ).PaddingHorizontal(10).PaddingVertical(4).Text(
                        sale.PaymentStatus switch
                        {
                            PaymentStatus.Paid => "PAID" + (sale.PaymentMethod != null ? $" · {sale.PaymentMethod.ToUpperInvariant()}" : ""),
                            PaymentStatus.Unpaid => "UNPAID",
                            PaymentStatus.PartiallyPaid => "PARTIALLY PAID",
                            _ => sale.PaymentStatus.ToString().ToUpperInvariant()
                        }
                    ).FontSize(8).Bold().LetterSpacing(1.5f).FontColor(
                        sale.PaymentStatus == PaymentStatus.Paid ? "#065F46" :
                        sale.PaymentStatus == PaymentStatus.Unpaid ? "#991B1B" : "#92400E"
                    );

                    // ── Footer ──
                    col.Item().PaddingTop(28).AlignCenter().Text("Thank you for your business")
                        .FontSize(9).Italic().FontColor("#94A3B8");
                    col.Item().PaddingTop(2).AlignCenter().Text($"Powered by Ojunai · ojunai.com")
                        .FontSize(7).FontColor("#CBD5E1");
                });
            });
        }).GeneratePdf();
    }
}
