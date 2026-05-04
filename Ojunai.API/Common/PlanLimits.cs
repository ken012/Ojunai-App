namespace Ojunai.API.Common;

public class PlanConfig
{
    public int MaxProducts { get; init; } = -1; // -1 = unlimited
    public int MaxMessagesPerMonth { get; init; } = -1;
    public int MaxStaff { get; init; } = -1; // includes owner
    public decimal PricePerMonth { get; init; }
    public int TrialDays { get; init; }
    public bool HasLedger { get; init; } = true;
    public bool HasCsvImport { get; init; } = true;
    public bool HasAdvancedReports { get; init; } = true;
    public bool HasMonthlyCharts { get; init; } = true;
    public bool HasStockHolds { get; init; } = true;
    public bool HasMultiBranch { get; init; } = false;
    public bool HasApiAccess { get; init; } = false;
    public bool HasCustomExports { get; init; } = false;
}

public static class PlanLimits
{
    private static readonly Dictionary<string, PlanConfig> Plans = new(StringComparer.OrdinalIgnoreCase)
    {
        ["starter"] = new PlanConfig
        {
            MaxProducts = 30,
            MaxMessagesPerMonth = 150,
            MaxStaff = 1,
            PricePerMonth = 3500,
            TrialDays = 30,
            HasLedger = true,
            HasCsvImport = false,
            HasAdvancedReports = false,
            HasMonthlyCharts = false,
            HasStockHolds = false,
        },
        ["shop"] = new PlanConfig
        {
            MaxProducts = -1,
            MaxMessagesPerMonth = 850,
            MaxStaff = 4,
            PricePerMonth = 7500,
            TrialDays = 30,
            HasLedger = true,
            HasCsvImport = false,
            HasAdvancedReports = false,
            HasMonthlyCharts = false,
            HasStockHolds = true,
        },
        ["pro"] = new PlanConfig
        {
            MaxProducts = -1,
            MaxMessagesPerMonth = -1,
            MaxStaff = 11,
            PricePerMonth = 12500,
            TrialDays = 30,
            HasLedger = true,
            HasCsvImport = true,
            HasAdvancedReports = true,
            HasMonthlyCharts = true,
            HasStockHolds = true,
        },
        ["business"] = new PlanConfig
        {
            MaxProducts = -1,
            MaxMessagesPerMonth = -1,
            MaxStaff = -1,
            PricePerMonth = 30000,
            HasLedger = true,
            HasCsvImport = true,
            HasAdvancedReports = true,
            HasMonthlyCharts = true,
            HasStockHolds = true,
            HasMultiBranch = true,
            HasApiAccess = true,
            HasCustomExports = true,
        },
    };

    public static PlanConfig Get(string? plan)
        => Plans.TryGetValue(plan ?? "starter", out var config) ? config : Plans["starter"];

    public static readonly string[] AllPlans = { "starter", "shop", "pro", "business" };
}
