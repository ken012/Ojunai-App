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
    public bool? ConfirmLargeSalesTelegram { get; set; }
    public decimal? ConfirmLargeSaleThresholdTelegram { get; set; }
    public bool? ConfirmLargeSalesMessenger { get; set; }
    public decimal? ConfirmLargeSaleThresholdMessenger { get; set; }
    public bool? VariantsEnabled { get; set; }

    // ── Per-source large-sale alert toggles ──────────
    // Owners can mute large-sale alerts independently per source channel.
    public bool? LargeSaleAlertWhatsApp { get; set; }
    public bool? LargeSaleAlertTelegram { get; set; }
    public bool? LargeSaleAlertMessenger { get; set; }
    public bool? LargeSaleAlertDashboard { get; set; }

    // ── Dashboard alert toggles ──────────────────────
    public bool? AlertDashboardLowStock { get; set; }
    public bool? AlertDashboardDailySummary { get; set; }
    public bool? AlertDashboardLargeSale { get; set; }
    public bool? AlertDashboardAgedReceivable { get; set; }
    public bool? AlertDashboardStaffChanges { get; set; }
    [Range(0, 999999999)] public decimal? DailySalesGoal { get; set; }

    [Range(0, 1)] public decimal? BackgroundImageOpacity { get; set; }

    // ── Receipts ─────────────────────────────────────
    [MaxLength(300)] public string? Address { get; set; }
    public bool? VatEnabled { get; set; }
    [Range(0, 100)] public decimal? VatRate { get; set; }
    [MaxLength(50)] public string? TaxId { get; set; }
    [MaxLength(80)] public string? ReceiptHeaderText { get; set; }
    [MaxLength(200)] public string? ReceiptFooterText { get; set; }
    [MaxLength(7)] public string? ReceiptAccentColor { get; set; }
}

/// <summary>Draft receipt settings sent to /business/receipt-preview to render a sample PDF.</summary>
public class ReceiptPreviewRequest
{
    [MaxLength(80)] public string? ReceiptHeaderText { get; set; }
    [MaxLength(200)] public string? ReceiptFooterText { get; set; }
    [MaxLength(7)] public string? ReceiptAccentColor { get; set; }
    [MaxLength(50)] public string? TaxId { get; set; }
    public bool VatEnabled { get; set; }
    [Range(0, 100)] public decimal VatRate { get; set; }
}
