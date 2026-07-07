using System.ComponentModel.DataAnnotations;

namespace Ojunai.API.DTOs.Purchasing;

public class CreatePurchaseOrderRequest
{
    public Guid? SupplierId { get; set; }
    [MaxLength(200)] public string? SupplierName { get; set; }
    [MaxLength(1000)] public string? Notes { get; set; }
    public DateTime? ExpectedAtUtc { get; set; }
    [MinLength(1)] public List<PurchaseOrderItemInput> Items { get; set; } = new();
}

public class UpdatePurchaseOrderRequest
{
    public Guid? SupplierId { get; set; }
    [MaxLength(200)] public string? SupplierName { get; set; }
    [MaxLength(1000)] public string? Notes { get; set; }
    public DateTime? ExpectedAtUtc { get; set; }
    /// <summary>When provided, replaces the PO's line items wholesale. Only allowed while Draft.</summary>
    public List<PurchaseOrderItemInput>? Items { get; set; }
}

public class PurchaseOrderItemInput
{
    public Guid? ProductId { get; set; }
    [Required, MinLength(1), MaxLength(200)] public string ProductName { get; set; } = string.Empty;
    [MaxLength(50)] public string? Unit { get; set; }
    [Range(0.0001, 9999999)] public decimal QuantityOrdered { get; set; }
    [Range(0, 999999999)] public decimal UnitCost { get; set; }
}

/// <summary>One line of a receive. Quantity received now (can be partial across multiple receives).</summary>
public class ReceivePurchaseOrderItemInput
{
    [Required] public Guid ItemId { get; set; }
    [Range(0, 9999999)] public decimal QuantityReceived { get; set; }
}

public class ReceivePurchaseOrderRequest
{
    /// <summary>Per-line quantities received. Omit a line to receive 0 for it.</summary>
    public List<ReceivePurchaseOrderItemInput> Lines { get; set; } = new();
    /// <summary>Create a payable to the supplier for the value received this time. Default true.</summary>
    public bool CreatePayable { get; set; } = true;
}

public class PurchaseOrderItemDto
{
    public Guid Id { get; set; }
    public Guid? ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Unit { get; set; } = "unit";
    public decimal QuantityOrdered { get; set; }
    public decimal QuantityReceived { get; set; }
    public decimal UnitCost { get; set; }
    public decimal LineTotal { get; set; }
}

public class PurchaseOrderDto
{
    public Guid Id { get; set; }
    public string PoNumber { get; set; } = string.Empty;
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Currency { get; set; } = "NGN";
    public decimal TotalAmount { get; set; }
    public string? Notes { get; set; }
    public DateTime? ExpectedAtUtc { get; set; }
    public string? RecordedByName { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public DateTime? ReceivedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public List<PurchaseOrderItemDto> Items { get; set; } = new();
}
