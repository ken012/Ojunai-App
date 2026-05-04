namespace Ojunai.API.Models;

public class Sale
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BusinessId { get; set; }
    public Guid? ContactId { get; set; }
    public decimal TotalAmount { get; set; }
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Paid;
    public string? PaymentMethod { get; set; }
    public string? Notes { get; set; }
    public string Source { get; set; } = "Manual";
    public Guid? RecordedByUserId { get; set; }
    public string? RecordedByName { get; set; }
    public bool IsDeleted { get; set; } = false;
    public string? DeleteReason { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Business Business { get; set; } = null!;
    public Contact? Contact { get; set; }
    public ICollection<SaleItem> Items { get; set; } = new List<SaleItem>();
}

public enum PaymentStatus { Paid = 1, Unpaid = 2, PartiallyPaid = 3 }

public class SaleItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SaleId { get; set; }
    public Guid ProductId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }

    public Sale Sale { get; set; } = null!;
    public Product Product { get; set; } = null!;
}
