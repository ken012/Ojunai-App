using System.ComponentModel.DataAnnotations;

namespace Ojunai.API.DTOs.Expenses;

public class CreateExpenseRequest
{
    [Required, MaxLength(100)] public string Category { get; set; } = "General";
    [Range(0.01, 999999999)] public decimal Amount { get; set; }
    [MaxLength(500)] public string? Notes { get; set; }
    [MaxLength(200)] public string? PaidTo { get; set; }
    [MaxLength(20)] public string? ExpenseType { get; set; }
    [MaxLength(50)] public string? PaymentMethod { get; set; }
    public DateTime? ExpenseDate { get; set; }
}

public class UpdateExpenseRequest
{
    [MaxLength(100)] public string? Category { get; set; }
    [Range(0.01, 999999999)] public decimal? Amount { get; set; }
    [MaxLength(500)] public string? Notes { get; set; }
    [MaxLength(200)] public string? PaidTo { get; set; }
    [MaxLength(20)] public string? ExpenseType { get; set; }
    [MaxLength(50)] public string? PaymentMethod { get; set; }
}

public class ExpenseDto
{
    public Guid Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public string ExpenseType { get; set; } = "operating";
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
    public string? PaidTo { get; set; }
    public string? PaymentMethod { get; set; }
    public string Source { get; set; } = "Manual";
    public DateTime CreatedAtUtc { get; set; }
}

public class ExpenseFiltersDto
{
    public List<string> Categories { get; set; } = new();
    public List<string> PaymentMethods { get; set; } = new();
    public List<string> Sources { get; set; } = new();
}
