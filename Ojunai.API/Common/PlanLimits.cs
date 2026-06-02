namespace Ojunai.API.Common;

public class PlanConfig
{
    public int MaxProducts { get; init; }
    public int MaxMessagesPerMonth { get; init; }
    public int MaxStaff { get; init; }
    public decimal PricePerMonth { get; init; }
    public int TrialDays { get; init; }
    public bool HasLedger { get; init; }
    public bool HasCsvImport { get; init; }
    public bool HasAdvancedReports { get; init; }
    public bool HasMonthlyCharts { get; init; }
    public bool HasStockHolds { get; init; }
    public bool HasMultiBranch { get; init; }
    public bool HasApiAccess { get; init; }
    public bool HasCustomExports { get; init; }
    public bool HasCustomBranding { get; init; }
}

/// <summary>
/// Feature limits per plan tier. Display prices come from <see cref="BillingConfig.Prices"/>;
/// the PricePerMonth field here is used by legacy paths that haven't been migrated to the
/// currency-aware lookup yet. Tier codes match what's stored in Business.Plan.
/// </summary>
public static class PlanLimits
{
    private static readonly Dictionary<string, PlanConfig> Plans = new(StringComparer.OrdinalIgnoreCase)
    {
        ["starter"] = new PlanConfig
        {
            MaxProducts = 30,
            MaxMessagesPerMonth = 150,
            MaxStaff = 1,
            PricePerMonth = 0,
            TrialDays = 30,
            HasLedger = true,
            HasCsvImport = false,
            HasAdvancedReports = false,
            HasMonthlyCharts = false,
            HasStockHolds = false,
        },
        ["lite"] = new PlanConfig
        {
            MaxProducts = -1,
            MaxMessagesPerMonth = 500,
            MaxStaff = 1,
            PricePerMonth = 12500,
            TrialDays = 30,
            HasLedger = true,
            HasCsvImport = false,
            HasAdvancedReports = false,
            HasMonthlyCharts = false,
            HasStockHolds = true,
        },
        ["operator"] = new PlanConfig
        {
            MaxProducts = -1,
            MaxMessagesPerMonth = 1500,
            MaxStaff = 1,
            PricePerMonth = 29999,
            TrialDays = 30,
            HasLedger = true,
            HasCsvImport = false,
            HasAdvancedReports = false,
            HasMonthlyCharts = true,
            HasStockHolds = true,
        },
        ["pro"] = new PlanConfig
        {
            MaxProducts = -1,
            MaxMessagesPerMonth = 4000,
            MaxStaff = 3,
            PricePerMonth = 72500,
            TrialDays = 30,
            HasLedger = true,
            HasCsvImport = true,
            HasAdvancedReports = true,
            HasMonthlyCharts = true,
            HasStockHolds = true,
            HasCustomBranding = true,
        },
        ["scale"] = new PlanConfig
        {
            MaxProducts = -1,
            MaxMessagesPerMonth = -1,
            MaxStaff = 6,
            PricePerMonth = 155000,
            HasLedger = true,
            HasCsvImport = true,
            HasAdvancedReports = true,
            HasMonthlyCharts = true,
            HasStockHolds = true,
            HasMultiBranch = true,
            HasApiAccess = false,
            HasCustomExports = true,
            HasCustomBranding = true,
        },
    };

    public static PlanConfig Get(string? plan)
        => Plans.TryGetValue(plan ?? "starter", out var config) ? config : Plans["starter"];

    public static readonly string[] AllPlans = { "starter", "lite", "operator", "pro", "scale" };
}
