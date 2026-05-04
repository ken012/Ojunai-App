using System.ComponentModel.DataAnnotations;

namespace Ojunai.API.DTOs.Business;

public class UpdateBusinessRequest
{
    [MaxLength(100)] public string? BusinessType { get; set; }
    [MaxLength(10)] public string? Currency { get; set; }
    [MaxLength(100)] public string? State { get; set; }
    [MaxLength(100)] public string? City { get; set; }
    [MaxLength(100)] public string? Country { get; set; }
    [Range(0, 999999999)] public decimal? LargeSaleThreshold { get; set; }
    public List<string>? CustomCategories { get; set; }
    public bool? AlertLowStock { get; set; }
    public bool? AlertDailySummary { get; set; }
    public bool? AlertLargeSale { get; set; }
    public bool? ConfirmLargeSales { get; set; }
    public decimal? ConfirmLargeSaleThreshold { get; set; }

    // ── Receipts ─────────────────────────────────────
    [MaxLength(300)] public string? Address { get; set; }
    public bool? VatEnabled { get; set; }
    [Range(0, 100)] public decimal? VatRate { get; set; }
    [MaxLength(50)] public string? TaxId { get; set; }
    [MaxLength(80)] public string? ReceiptHeaderText { get; set; }
    [MaxLength(200)] public string? ReceiptFooterText { get; set; }
    [MaxLength(7)] public string? ReceiptAccentColor { get; set; }
}
