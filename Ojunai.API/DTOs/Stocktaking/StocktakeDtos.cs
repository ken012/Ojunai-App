using System.ComponentModel.DataAnnotations;

namespace Ojunai.API.DTOs.Stocktaking;

public class CreateStocktakeRequest
{
    /// <summary>Optional category to scope the count to. Null/empty = all active products.</summary>
    [MaxLength(100)] public string? Category { get; set; }
    [MaxLength(1000)] public string? Notes { get; set; }
}

public class CountInput
{
    [Required] public Guid ItemId { get; set; }
    /// <summary>Counted physical quantity. Null clears a previously-entered count.</summary>
    public decimal? CountedQuantity { get; set; }
}

public class SaveCountsRequest
{
    public List<CountInput> Counts { get; set; } = new();
}

public class StocktakeItemDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Unit { get; set; } = "unit";
    public decimal SystemQuantity { get; set; }
    public decimal? CountedQuantity { get; set; }
    public decimal UnitCost { get; set; }
    /// <summary>counted − system (null until counted). Positive = surplus, negative = shortage.</summary>
    public decimal? Variance { get; set; }
    /// <summary>Variance × unit cost (null until counted). Negative = shrinkage cost.</summary>
    public decimal? VarianceValue { get; set; }
}

public class StocktakeDto
{
    public Guid Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Scope { get; set; }
    public string? Notes { get; set; }
    public string? RecordedByName { get; set; }
    public int TotalItems { get; set; }
    public int CountedItems { get; set; }
    public int VarianceItems { get; set; }
    public decimal NetVarianceValue { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public List<StocktakeItemDto> Items { get; set; } = new();
}
