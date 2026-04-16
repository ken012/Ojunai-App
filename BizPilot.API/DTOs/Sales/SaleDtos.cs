using System.ComponentModel.DataAnnotations;
using BizPilot.API.Models;

namespace BizPilot.API.DTOs.Sales;

public class CreateSaleRequest
{
    [Required, MinLength(1)] public List<SaleItemRequest> Items { get; set; } = new();
    public Guid? ContactId { get; set; }
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Paid;
    [MaxLength(50)] public string? PaymentMethod { get; set; }
    [MaxLength(500)] public string? Notes { get; set; }
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
    public string PaymentStatus { get; set; } = string.Empty;
    public string? PaymentMethod { get; set; }
    public string? Notes { get; set; }
    public string? CustomerName { get; set; }
    public List<SaleItemDto> Items { get; set; } = new();
    public string Source { get; set; } = "Manual";
    public DateTime CreatedAtUtc { get; set; }
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
    public int ItemCount { get; set; }
    public string? ItemSummary { get; set; }
    public string? CustomerName { get; set; }
    public string? RecordedByName { get; set; }
    public string Source { get; set; } = "Manual";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}
