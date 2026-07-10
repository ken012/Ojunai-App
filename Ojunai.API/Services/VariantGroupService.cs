using System.Text.Json;
using Ojunai.API.Data;
using Ojunai.API.DTOs.Variants;
using Ojunai.API.Models;
using Ojunai.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Services;

/// <summary>
/// Variant "styles": a grouping layer over ordinary products. Creating a group generates one product
/// per option-value combination; each is a full, sellable/stockable Product tagged with VariantGroupId.
/// Additive — nothing in sales/inventory/reports depends on this.
/// </summary>
public class VariantGroupService : IVariantGroupService
{
    private readonly AppDbContext _db;
    public VariantGroupService(AppDbContext db) => _db = db;

    public async Task<VariantGroupDto> CreateAsync(Guid businessId, CreateVariantGroupRequest request)
    {
        var name = request.Name.Trim();
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("A style name is required.");
        if (request.Axes == null || request.Axes.Count == 0) throw new ArgumentException("Add at least one option (e.g. Size).");

        // Clean axes: trim, drop empties, dedupe values per axis.
        var axes = request.Axes
            .Select(a => new VariantAxisInput
            {
                Name = a.Name.Trim(),
                Values = a.Values.Select(v => v.Trim()).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            })
            .Where(a => a.Name.Length > 0 && a.Values.Count > 0)
            .ToList();
        if (axes.Count == 0) throw new ArgumentException("Each option needs a name and at least one value.");

        var combos = CartesianProduct(axes);
        if (combos.Count > 200) throw new InvalidOperationException("That's over 200 variants. Reduce the options and try again.");

        var unit = string.IsNullOrWhiteSpace(request.Unit) ? "unit" : request.Unit!.Trim();
        var category = string.IsNullOrWhiteSpace(request.Category) ? null : request.Category!.Trim();

        var group = new VariantGroup
        {
            BusinessId = businessId,
            Name = name,
            Axes = JsonSerializer.Serialize(axes.Select(a => a.Name).ToList()),
            Category = category,
        };
        _db.VariantGroups.Add(group);

        foreach (var combo in combos)
        {
            _db.Products.Add(new Product
            {
                BusinessId = businessId,
                Name = $"{name} — {string.Join(" / ", combo.Values.Select(kv => kv.Value))}",
                Unit = unit,
                Category = category,
                SellingPrice = request.BaseSellingPrice,
                CostPrice = request.BaseCostPrice,
                CurrentStock = 0,
                LowStockThreshold = request.LowStockThreshold,
                VariantGroupId = group.Id,
                VariantOptions = JsonSerializer.Serialize(combo.Values),
                Source = "Manual",
            });
        }

        await _db.SaveChangesAsync();
        return await GetAsync(businessId, group.Id);
    }

    public async Task<List<VariantGroupDto>> ListAsync(Guid businessId)
    {
        var groups = await _db.VariantGroups
            .Where(g => g.BusinessId == businessId)
            .OrderByDescending(g => g.CreatedAtUtc)
            .ToListAsync();
        if (groups.Count == 0) return new();

        var ids = groups.Select(g => g.Id).ToList();
        var variants = await _db.Products
            .Where(p => p.BusinessId == businessId && p.IsActive && p.VariantGroupId != null && ids.Contains(p.VariantGroupId!.Value))
            .ToListAsync();
        var byGroup = variants.GroupBy(p => p.VariantGroupId!.Value).ToDictionary(g => g.Key, g => g.ToList());

        return groups.Select(g => ToDto(g, byGroup.GetValueOrDefault(g.Id) ?? new List<Product>(), includeVariants: false)).ToList();
    }

    public async Task<VariantGroupDto> GetAsync(Guid businessId, Guid groupId)
    {
        var group = await _db.VariantGroups.FirstOrDefaultAsync(g => g.Id == groupId && g.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Variant style not found.");
        var variants = await _db.Products
            .Where(p => p.BusinessId == businessId && p.IsActive && p.VariantGroupId == groupId)
            .OrderBy(p => p.Name)
            .ToListAsync();
        return ToDto(group, variants, includeVariants: true);
    }

    public async Task<VariantGroupDto> AddVariantAsync(Guid businessId, Guid groupId, AddVariantRequest request)
    {
        var group = await _db.VariantGroups.FirstOrDefaultAsync(g => g.Id == groupId && g.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Variant style not found.");
        var axisNames = JsonSerializer.Deserialize<List<string>>(group.Axes) ?? new List<string>();

        // Keep only recognised axes, in axis order, so the name/options are consistent.
        var options = new Dictionary<string, string>();
        foreach (var axis in axisNames)
        {
            if (request.Options.TryGetValue(axis, out var v) && !string.IsNullOrWhiteSpace(v))
                options[axis] = v.Trim();
        }
        if (options.Count == 0) throw new ArgumentException("Provide a value for at least one option.");

        var product = new Product
        {
            BusinessId = businessId,
            Name = $"{group.Name} — {string.Join(" / ", options.Values)}",
            Unit = "unit",
            Category = group.Category,
            SellingPrice = request.SellingPrice,
            CostPrice = request.CostPrice,
            CurrentStock = 0,
            LowStockThreshold = request.LowStockThreshold,
            SKU = string.IsNullOrWhiteSpace(request.SKU) ? null : request.SKU!.Trim(),
            Barcode = string.IsNullOrWhiteSpace(request.Barcode) ? null : request.Barcode!.Trim(),
            VariantGroupId = groupId,
            VariantOptions = JsonSerializer.Serialize(options),
            Source = "Manual",
        };
        _db.Products.Add(product);
        await _db.SaveChangesAsync();
        return await GetAsync(businessId, groupId);
    }

    public async Task UngroupAsync(Guid businessId, Guid groupId)
    {
        var group = await _db.VariantGroups.FirstOrDefaultAsync(g => g.Id == groupId && g.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Variant style not found.");
        var members = await _db.Products
            .Where(p => p.BusinessId == businessId && p.VariantGroupId == groupId)
            .ToListAsync();
        foreach (var p in members) { p.VariantGroupId = null; p.VariantOptions = null; } // become standalone, kept
        _db.VariantGroups.Remove(group);
        await _db.SaveChangesAsync();
    }

    // ── helpers ────────────────────────────────────────────────────────────────
    private sealed record Combo(Dictionary<string, string> Values);

    private static List<Combo> CartesianProduct(List<VariantAxisInput> axes)
    {
        var result = new List<Combo> { new(new Dictionary<string, string>()) };
        foreach (var axis in axes)
        {
            var next = new List<Combo>();
            foreach (var partial in result)
                foreach (var value in axis.Values)
                {
                    var d = new Dictionary<string, string>(partial.Values) { [axis.Name] = value };
                    next.Add(new Combo(d));
                }
            result = next;
        }
        return result;
    }

    private static VariantGroupDto ToDto(VariantGroup g, List<Product> variants, bool includeVariants)
    {
        var axisNames = JsonSerializer.Deserialize<List<string>>(g.Axes) ?? new List<string>();
        var prices = variants.Where(v => v.SellingPrice.HasValue).Select(v => v.SellingPrice!.Value).ToList();

        // Axis values, derived from the members so the picker shows what actually exists.
        var axes = axisNames.Select(name =>
        {
            var values = variants
                .Select(v => ParseOptions(v.VariantOptions).GetValueOrDefault(name))
                .Where(x => !string.IsNullOrEmpty(x))
                .Select(x => x!)
                .Distinct()
                .ToList();
            return new VariantAxisDto { Name = name, Values = values };
        }).ToList();

        return new VariantGroupDto
        {
            Id = g.Id,
            Name = g.Name,
            Category = g.Category,
            Axes = axes,
            VariantCount = variants.Count,
            TotalStock = variants.Sum(v => v.CurrentStock),
            LowStockCount = variants.Count(v => v.CurrentStock <= v.LowStockThreshold),
            MinPrice = prices.Count > 0 ? prices.Min() : null,
            MaxPrice = prices.Count > 0 ? prices.Max() : null,
            CreatedAtUtc = g.CreatedAtUtc,
            Variants = includeVariants
                ? variants.Select(v => new VariantDto
                {
                    ProductId = v.Id,
                    Name = v.Name,
                    Options = ParseOptions(v.VariantOptions),
                    SKU = v.SKU,
                    Barcode = v.Barcode,
                    Unit = v.Unit,
                    SellingPrice = v.SellingPrice,
                    CostPrice = v.CostPrice,
                    CurrentStock = v.CurrentStock,
                    LowStockThreshold = v.LowStockThreshold,
                    IsLowStock = v.CurrentStock <= v.LowStockThreshold,
                }).ToList()
                : new List<VariantDto>(),
        };
    }

    private static Dictionary<string, string> ParseOptions(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new();
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new(); }
        catch { return new(); }
    }
}
