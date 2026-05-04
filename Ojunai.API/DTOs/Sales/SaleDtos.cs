using System.ComponentModel.DataAnnotations;
using Ojunai.API.Models;

namespace Ojunai.API.DTOs.Sales;

public class CreateSaleRequest
{
    [Required, MinLength(1)] public List<SaleItemRequest> Items { get; set; } = new();
    public Guid? ContactId { get; set; }
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Paid;
    [MaxLength(50)] public string? PaymentMethod { get; set; }
    [MaxLength(500)] public string? Notes { get; set; }
    public DateTime? SaleDate { get; set; }
    /// <summary>Optional VAT amount included in the totals (computed by the dashboard or omitted if business has VAT off).</summary>
    [Range(0, 999999999)] public decimal? VatAmount { get; set; }
}

public class SaleItemRequest
{
    [Required] public Guid ProductId { get; set; }
    [Range(0.001, 999999)] public decimal Quantity { get; set; }
    [Range(0, 999999999)] public decimal UnitPrice { get; set; }
}

public class SaleDto
{
    public Guid Id { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? PaymentMethod { get; set; }
    public string? Notes { get; set; }
    public string? CustomerName { get; set; }
    public string? RecordedByName { get; set; }
    public string? Source { get; set; } = "Manual";
    public string? ReceiptNumber { get; set; }
    public List<SaleItemDto> Items { get; set; } = new();
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Contact's overall outstanding receivable balance (null if no contact linked).</summary>
    public decimal? ContactBalance { get; set; }
    /// <summary>Earliest unpaid receivable due date for this contact (null if none).</summary>
    public DateTime? DueDate { get; set; }
}

public class SaleItemDto
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }
}

public class SaleSummaryDto
{
    public Guid Id { get; set; }
    public decimal TotalAmount { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? PaymentMethod { get; set; }
    public int ItemCount { get; set; }
    public string? ItemSummary { get; set; }
    public string? CustomerName { get; set; }
    public string? RecordedByName { get; set; }
    public string Source { get; set; } = "Manual";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}
