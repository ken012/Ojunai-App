using BizPilot.API.Common;
using BizPilot.API.Data;
using BizPilot.API.DTOs.Dashboard;
using BizPilot.API.DTOs.Ledger;
using BizPilot.API.DTOs.Products;
using BizPilot.API.DTOs.Reports;
using BizPilot.API.Models;
using BizPilot.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BizPilot.API.Services;

public partial class ReportService : IReportService
{
    private readonly AppDbContext _db;

    public ReportService(AppDbContext db) => _db = db;

    public async Task<DashboardOverviewDto> GetDashboardOverviewAsync(Guid businessId)
    {
        var todayUtc = DateTime.UtcNow.Date;
        var tomorrowUtc = todayUtc.AddDays(1);

        var todaySales = await _db.Sales
            .Where(s => s.BusinessId == businessId && s.CreatedAtUtc >= todayUtc && s.CreatedAtUtc < tomorrowUtc)
            .ToListAsync();

        var todayExpenses = await _db.Expenses
            .Where(e => e.BusinessId == businessId && e.CreatedAtUtc >= todayUtc && e.CreatedAtUtc < tomorrowUtc)
            .SumAsync(e => e.Amount);

        var ledger = await _db.LedgerEntries
            .Where(e => e.BusinessId == businessId)
            .ToListAsync();

        var outstandingReceivables = ledger.Where(e => e.EntryType == LedgerEntryType.Receivable).Sum(e => e.Amount)
                                   - ledger.Where(e => e.EntryType == LedgerEntryType.ReceivablePayment).Sum(e => e.Amount);
        var outstandingPayables = ledger.Where(e => e.EntryType == LedgerEntryType.Payable).Sum(e => e.Amount)
                                - ledger.Where(e => e.EntryType == LedgerEntryType.PayablePayment).Sum(e => e.Amount);

        var lowStockCount = await _db.Products
            .CountAsync(p => p.BusinessId == businessId && p.IsActive && p.CurrentStock <= p.LowStockThreshold);

        // 7-day trend
        var sevenDaysAgo = todayUtc.AddDays(-6);
        var salesTrend = await BuildSalesTrendAsync(businessId, sevenDaysAgo, todayUtc);
        var expenseTrend = await BuildExpenseTrendAsync(businessId, sevenDaysAgo, todayUtc);

        return new DashboardOverviewDto
        {
            TodaySales = todaySales.Sum(s => s.TotalAmount),
            TodaySaleCount = todaySales.Count,
            TodayExpenses = todayExpenses,
            OutstandingReceivables = Math.Max(0, outstandingReceivables),
            OutstandingPayables = Math.Max(0, outstandingPayables),
            LowStockCount = lowStockCount,
            SalesTrend = salesTrend,
            ExpenseTrend = expenseTrend
        };
    }

    public async Task<List<RecentActivityDto>> GetRecentActivityAsync(Guid businessId, int limit)
    {
        var activities = new List<RecentActivityDto>();

        var sales = await _db.Sales
            .Where(s => s.BusinessId == businessId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .Take(limit)
            .Select(s => new RecentActivityDto
            {
                Type = "sale",
                Description = $"Sale recorded",
                Amount = s.TotalAmount,
                CreatedAtUtc = s.CreatedAtUtc
            }).ToListAsync();

        var expenses = await _db.Expenses
            .Where(e => e.BusinessId == businessId)
            .OrderByDescending(e => e.CreatedAtUtc)
            .Take(limit)
            .Select(e => new RecentActivityDto
            {
                Type = "expense",
                Description = $"{e.Category} expense",
                Amount = e.Amount,
                CreatedAtUtc = e.CreatedAtUtc
            }).ToListAsync();

        activities.AddRange(sales);
        activities.AddRange(expenses);

        return activities.OrderByDescending(a => a.CreatedAtUtc).Take(limit).ToList();
    }

    public async Task<DailySummaryDto> GetDailySummaryAsync(Guid businessId, DateOnly? date)
    {
        var targetDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var start = targetDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = start.AddDays(1);

        var sales = await _db.Sales
            .Where(s => s.BusinessId == businessId && s.CreatedAtUtc >= start && s.CreatedAtUtc < end)
            .ToListAsync();

        var totalExpenses = await _db.Expenses
            .Where(e => e.BusinessId == businessId && e.CreatedAtUtc >= start && e.CreatedAtUtc < end)
            .SumAsync(e => e.Amount);

        var ledger = await _db.LedgerEntries
            .Where(e => e.BusinessId == businessId)
            .ToListAsync();

        var receivables = ledger.Where(e => e.EntryType == LedgerEntryType.Receivable).Sum(e => e.Amount)
                        - ledger.Where(e => e.EntryType == LedgerEntryType.ReceivablePayment).Sum(e => e.Amount);
        var payables = ledger.Where(e => e.EntryType == LedgerEntryType.Payable).Sum(e => e.Amount)
                    - ledger.Where(e => e.EntryType == LedgerEntryType.PayablePayment).Sum(e => e.Amount);

        var lowStockItems = await _db.Products
            .Where(p => p.BusinessId == businessId && p.IsActive && p.CurrentStock <= p.LowStockThreshold)
            .Select(p => new ProductDto
            {
                Id = p.Id, Name = p.Name, SKU = p.SKU, Unit = p.Unit,
                CostPrice = p.CostPrice, SellingPrice = p.SellingPrice,
                CurrentStock = p.CurrentStock, LowStockThreshold = p.LowStockThreshold,
                IsLowStock = true, IsActive = p.IsActive, CreatedAtUtc = p.CreatedAtUtc
            })
            .ToListAsync();

        var totalSales = sales.Sum(s => s.TotalAmount);

        return new DailySummaryDto
        {
            Date = targetDate.ToString("yyyy-MM-dd"),
            TotalSales = totalSales,
            SaleCount = sales.Count,
            TotalExpenses = totalExpenses,
            NetCashIn = totalSales - totalExpenses,
            OutstandingReceivables = Math.Max(0, receivables),
            OutstandingPayables = Math.Max(0, payables),
            LowStockCount = lowStockItems.Count,
            LowStockItems = lowStockItems
        };
    }

    public async Task<WeeklySummaryDto> GetWeeklySummaryAsync(Guid businessId, DateOnly? weekStart)
    {
        var start = weekStart ?? GetMondayOfCurrentWeek();
        var end = start.AddDays(7);
        var startDt = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var endDt = end.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var totalSales = await _db.Sales
            .Where(s => s.BusinessId == businessId && s.CreatedAtUtc >= startDt && s.CreatedAtUtc < endDt)
            .SumAsync(s => s.TotalAmount);

        var totalExpenses = await _db.Expenses
            .Where(e => e.BusinessId == businessId && e.CreatedAtUtc >= startDt && e.CreatedAtUtc < endDt)
            .SumAsync(e => e.Amount);

        var topProducts = await _db.SaleItems
            .Include(i => i.Product)
            .Include(i => i.Sale)
            .Where(i => i.Sale.BusinessId == businessId && i.Sale.CreatedAtUtc >= startDt && i.Sale.CreatedAtUtc < endDt)
            .GroupBy(i => new { i.ProductId, i.Product.Name, i.Product.Unit })
            .Select(g => new TopProductDto
            {
                ProductId = g.Key.ProductId,
                ProductName = g.Key.Name,
                Unit = g.Key.Unit,
                TotalQuantitySold = g.Sum(i => i.Quantity),
                TotalRevenue = g.Sum(i => i.TotalPrice)
            })
            .OrderByDescending(p => p.TotalRevenue)
            .Take(5)
            .ToListAsync();

        var lowStockItems = await _db.Products
            .Where(p => p.BusinessId == businessId && p.IsActive && p.CurrentStock <= p.LowStockThreshold)
            .Select(p => new ProductDto
            {
                Id = p.Id, Name = p.Name, SKU = p.SKU, Unit = p.Unit,
                CostPrice = p.CostPrice, SellingPrice = p.SellingPrice,
                CurrentStock = p.CurrentStock, LowStockThreshold = p.LowStockThreshold,
                IsLowStock = true, IsActive = p.IsActive, CreatedAtUtc = p.CreatedAtUtc
            })
            .ToListAsync();

        var ledger = await _db.LedgerEntries
            .Include(e => e.Contact)
            .Where(e => e.BusinessId == businessId)
            .ToListAsync();

        var topDebtors = ledger
            .GroupBy(e => new { e.ContactId, e.Contact.Name, e.Contact.Type })
            .Select(g => new
            {
                g.Key.ContactId, g.Key.Name, g.Key.Type,
                NetReceivable = g.Where(e => e.EntryType == LedgerEntryType.Receivable).Sum(e => e.Amount)
                              - g.Where(e => e.EntryType == LedgerEntryType.ReceivablePayment).Sum(e => e.Amount)
            })
            .Where(x => x.NetReceivable > 0)
            .OrderByDescending(x => x.NetReceivable)
            .Take(5)
            .Select(x => new OutstandingBalanceDto
            {
                ContactId = x.ContactId,
                ContactName = x.Name,
                ContactType = x.Type.ToString(),
                TotalReceivable = x.NetReceivable,
                NetBalance = x.NetReceivable
            })
            .ToList();

        var hasCostData = await _db.Products.AnyAsync(p => p.BusinessId == businessId && p.CostPrice.HasValue);
        decimal estimatedProfit = 0;
        if (hasCostData)
        {
            var salesWithCost = await _db.SaleItems
                .Include(i => i.Product)
                .Include(i => i.Sale)
                .Where(i => i.Sale.BusinessId == businessId && i.Sale.CreatedAtUtc >= startDt && i.Sale.CreatedAtUtc < endDt && i.Product.CostPrice.HasValue)
                .ToListAsync();
            estimatedProfit = salesWithCost.Sum(i => (i.UnitPrice - i.Product.CostPrice!.Value) * i.Quantity) - totalExpenses;
        }

        return new WeeklySummaryDto
        {
            WeekStart = start.ToString("yyyy-MM-dd"),
            WeekEnd = end.AddDays(-1).ToString("yyyy-MM-dd"),
            TotalSales = totalSales,
            TotalExpenses = totalExpenses,
            EstimatedProfit = estimatedProfit,
            IsProfitEstimate = !hasCostData,
            TopProducts = topProducts,
            LowStockItems = lowStockItems,
            TopDebtors = topDebtors,
            TopSupplierBalances = new List<OutstandingBalanceDto>()
        };
    }

    public async Task<CashPositionDto> GetCashPositionAsync(Guid businessId)
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var totalSales = await _db.Sales
            .Where(s => s.BusinessId == businessId && s.CreatedAtUtc >= monthStart)
            .SumAsync(s => s.TotalAmount);

        var totalExpenses = await _db.Expenses
            .Where(e => e.BusinessId == businessId && e.CreatedAtUtc >= monthStart)
            .SumAsync(e => e.Amount);

        var ledger = await _db.LedgerEntries
            .Where(e => e.BusinessId == businessId)
            .ToListAsync();

        var receivables = ledger.Where(e => e.EntryType == LedgerEntryType.Receivable).Sum(e => e.Amount)
                        - ledger.Where(e => e.EntryType == LedgerEntryType.ReceivablePayment).Sum(e => e.Amount);
        var payables = ledger.Where(e => e.EntryType == LedgerEntryType.Payable).Sum(e => e.Amount)
                    - ledger.Where(e => e.EntryType == LedgerEntryType.PayablePayment).Sum(e => e.Amount);

        var cashIn = totalSales - totalExpenses;

        return new CashPositionDto
        {
            TotalSalesThisMonth = totalSales,
            TotalExpensesThisMonth = totalExpenses,
            EstimatedCashIn = cashIn,
            OutstandingReceivables = Math.Max(0, receivables),
            OutstandingPayables = Math.Max(0, payables),
            NetPosition = cashIn + Math.Max(0, receivables) - Math.Max(0, payables),
            IsEstimate = true
        };
    }

    public async Task<DashboardInsightsDto> GetInsightsAsync(Guid businessId)
    {
        var nowUtc = DateTime.UtcNow;
        var thirtyDaysAgo = nowUtc.Date.AddDays(-29);
        var fourteenDaysAgo = nowUtc.Date.AddDays(-13);

        // 1. Top Products (last 30 days)
        var topProducts = await _db.SaleItems
            .Include(i => i.Sale)
            .Include(i => i.Product)
            .Where(i => i.Sale.BusinessId == businessId && i.Sale.CreatedAtUtc >= thirtyDaysAgo)
            .GroupBy(i => new { i.Product.Name, i.Product.Unit })
            .Select(g => new TopProductInsightDto
            {
                ProductName = g.Key.Name,
                Unit = g.Key.Unit,
                Quantity = g.Sum(i => i.Quantity),
                Revenue = g.Sum(i => i.TotalPrice)
            })
            .OrderByDescending(p => p.Revenue)
            .Take(5)
            .ToListAsync();

        // 2. Expense breakdown by category (last 30 days)
        var expenseCategories = await _db.Expenses
            .Where(e => e.BusinessId == businessId && e.CreatedAtUtc >= thirtyDaysAgo)
            .GroupBy(e => e.Category)
            .Select(g => new CategoryBreakdownDto
            {
                Category = g.Key,
                Amount = g.Sum(e => e.Amount)
            })
            .OrderByDescending(c => c.Amount)
            .ToListAsync();

        // 3. Sales by payment status (last 30 days)
        var paymentStatus = await _db.Sales
            .Where(s => s.BusinessId == businessId && s.CreatedAtUtc >= thirtyDaysAgo)
            .GroupBy(s => s.PaymentStatus)
            .Select(g => new PaymentStatusBreakdownDto
            {
                Status = g.Key.ToString(),
                Amount = g.Sum(s => s.TotalAmount),
                Count = g.Count()
            })
            .ToListAsync();

        // 4. Receivables aging
        var receivableEntries = await _db.LedgerEntries
            .Where(e => e.BusinessId == businessId &&
                        (e.EntryType == LedgerEntryType.Receivable || e.EntryType == LedgerEntryType.ReceivablePayment))
            .ToListAsync();

        var byContact = receivableEntries
            .GroupBy(e => e.ContactId)
            .Select(g => new
            {
                ContactId = g.Key,
                Outstanding = g.Where(e => e.EntryType == LedgerEntryType.Receivable).Sum(e => e.Amount)
                            - g.Where(e => e.EntryType == LedgerEntryType.ReceivablePayment).Sum(e => e.Amount),
                OldestDate = g.Where(e => e.EntryType == LedgerEntryType.Receivable)
                              .OrderBy(e => e.CreatedAtUtc)
                              .Select(e => (DateTime?)e.CreatedAtUtc)
                              .FirstOrDefault()
            })
            .Where(c => c.Outstanding > 0 && c.OldestDate.HasValue)
            .ToList();

        var aging = new[] { "0-7 days", "8-30 days", "31-60 days", "60+ days" }
            .Select(b => new AgingBucketDto { Bucket = b, Amount = 0 })
            .ToList();

        foreach (var c in byContact)
        {
            var ageDays = (nowUtc - c.OldestDate!.Value).TotalDays;
            int bucketIdx = ageDays <= 7 ? 0 : ageDays <= 30 ? 1 : ageDays <= 60 ? 2 : 3;
            aging[bucketIdx].Amount += c.Outstanding;
        }

        // 5. Daily net cash flow (last 14 days)
        var salesByDay = await _db.Sales
            .Where(s => s.BusinessId == businessId && s.CreatedAtUtc >= fourteenDaysAgo)
            .GroupBy(s => s.CreatedAtUtc.Date)
            .Select(g => new { Date = g.Key, Amount = g.Sum(s => s.TotalAmount) })
            .ToListAsync();

        var expensesByDay = await _db.Expenses
            .Where(e => e.BusinessId == businessId && e.CreatedAtUtc >= fourteenDaysAgo)
            .GroupBy(e => e.CreatedAtUtc.Date)
            .Select(g => new { Date = g.Key, Amount = g.Sum(e => e.Amount) })
            .ToListAsync();

        var dailyNet = Enumerable.Range(0, 14)
            .Select(i => fourteenDaysAgo.AddDays(i))
            .Select(d =>
            {
                var sales = salesByDay.FirstOrDefault(s => s.Date == d)?.Amount ?? 0;
                var exp = expensesByDay.FirstOrDefault(e => e.Date == d)?.Amount ?? 0;
                return new DailyNetDto
                {
                    Date = d.ToString("yyyy-MM-dd"),
                    Sales = sales,
                    Expenses = exp,
                    Net = sales - exp
                };
            })
            .ToList();

        // 6. Top customers (last 30 days)
        var topCustomers = await _db.Sales
            .Include(s => s.Contact)
            .Where(s => s.BusinessId == businessId && s.CreatedAtUtc >= thirtyDaysAgo && s.ContactId != null)
            .GroupBy(s => new { s.ContactId, s.Contact!.Name })
            .Select(g => new TopCustomerDto
            {
                ContactName = g.Key.Name,
                Revenue = g.Sum(s => s.TotalAmount),
                SaleCount = g.Count()
            })
            .OrderByDescending(c => c.Revenue)
            .Take(5)
            .ToListAsync();

        return new DashboardInsightsDto
        {
            TopProducts = topProducts,
            ExpenseCategories = expenseCategories,
            PaymentStatus = paymentStatus,
            ReceivablesAging = aging,
            DailyNet = dailyNet,
            TopCustomers = topCustomers
        };
    }

    public async Task<PaginatedActivityResult> GetActivityFeedAsync(
        Guid businessId, string? type, int page, int pageSize, string? search, DateTime? startDate, DateTime? endDate)
    {
        var business = await _db.Businesses.FindAsync(businessId);
        var cs = BillingConfig.Symbol(business?.Currency);
        var activities = new List<ActivityFeedDto>();

        // Helper: generate a short human-readable reference ID from a GUID
        static string MakeRef(Guid id, string prefix) => $"{prefix}-{id.ToString("N")[..8].ToUpper()}";

        // Sales — IgnoreQueryFilters so voided sales appear too (with their own entry type)
        if (type == null || type == "sale" || type == "sale_voided")
        {
            var salesRaw = await _db.Sales
                .IgnoreQueryFilters()
                .Include(s => s.Items).ThenInclude(i => i.Product)
                .Include(s => s.Contact)
                .Where(s => s.BusinessId == businessId)
                .OrderByDescending(s => s.CreatedAtUtc)
                .ToListAsync();

            foreach (var s in salesRaw)
            {
                var itemSummary = s.Items.Count > 0
                    ? string.Join(", ", s.Items.Select(i => $"{i.Quantity:0.##} {i.Product.Unit} {i.Product.Name}"))
                    : "items";

                // Original sale entry — always shown
                activities.Add(new ActivityFeedDto
                {
                    Id = s.Id,
                    RefId = MakeRef(s.Id, "SL"),
                    Type = s.IsDeleted ? "sale_voided" : "sale",
                    Description = $"Sold {itemSummary} for {cs}{s.TotalAmount:N0}"
                        + (s.Contact != null ? $" to {s.Contact.Name}" : "")
                        + (s.PaymentStatus != PaymentStatus.Paid ? $" ({s.PaymentStatus})" : "")
                        + (s.IsDeleted ? " [VOIDED]" : ""),
                    Amount = s.TotalAmount,
                    ContactName = s.Contact?.Name,
                    RecordedBy = s.RecordedByName,
                    Source = s.Source,
                    PaymentStatus = s.PaymentStatus.ToString(),
                    PaymentMethod = s.PaymentMethod,
                    CreatedAtUtc = s.CreatedAtUtc
                });

                // Void event — separate entry at the void timestamp so it shows at the top of the feed
                if (s.IsDeleted && s.DeletedAtUtc.HasValue)
                {
                    activities.Add(new ActivityFeedDto
                    {
                        Id = s.Id,
                        RefId = MakeRef(s.Id, "SL"),
                        Type = "void_event",
                        Description = $"Sale voided: {itemSummary} ({cs}{s.TotalAmount:N0})"
                            + (s.Contact != null ? $" — {s.Contact.Name}" : "")
                            + $" — stock returned",
                        Amount = s.TotalAmount,
                        ContactName = s.Contact?.Name,
                        RecordedBy = s.RecordedByName,
                        Source = "Void",
                        Details = $"Original sale {MakeRef(s.Id, "SL")} from {s.CreatedAtUtc:dd MMM yyyy HH:mm}",
                        CreatedAtUtc = s.DeletedAtUtc.Value
                    });
                }
            }
        }

        // Expenses
        if (type == null || type == "expense")
        {
            var expenses = await _db.Expenses
                .IgnoreQueryFilters()
                .Where(e => e.BusinessId == businessId)
                .OrderByDescending(e => e.CreatedAtUtc)
                .ToListAsync();

            activities.AddRange(expenses.Select(e => new ActivityFeedDto
            {
                Id = e.Id,
                RefId = MakeRef(e.Id, "EX"),
                Type = e.IsDeleted ? "expense_voided" : "expense",
                Description = $"Expense: {e.Category}" + (e.Notes != null ? $" — {e.Notes}" : "") + (e.IsDeleted ? " [VOIDED]" : ""),
                Amount = e.Amount,
                ContactName = e.PaidTo,
                RecordedBy = e.RecordedByName,
                Source = e.Source,
                CreatedAtUtc = e.CreatedAtUtc
            }));
        }

        // Inventory transactions
        if (type == null || type == "inventory")
        {
            var inventory = await _db.InventoryTransactions
                .Include(t => t.Product)
                .Where(t => t.BusinessId == businessId)
                .OrderByDescending(t => t.CreatedAtUtc)
                .ToListAsync();

            activities.AddRange(inventory.Select(t => new ActivityFeedDto
            {
                Id = t.Id,
                RefId = MakeRef(t.Id, "INV"),
                Type = "inventory",
                Description = t.Type == InventoryTransactionType.StockIn
                    ? $"Restocked {t.Quantity:0.##} {t.Product.Unit} of {t.Product.Name}"
                    : t.Type == InventoryTransactionType.StockOut
                    ? $"Removed {t.Quantity:0.##} {t.Product.Unit} of {t.Product.Name}"
                    : t.Type == InventoryTransactionType.Damaged
                    ? $"Marked {t.Quantity:0.##} {t.Product.Unit} of {t.Product.Name} as damaged"
                    : $"Adjusted {t.Product.Name} stock by {t.Quantity:0.##} {t.Product.Unit}",
                Amount = t.UnitCost.HasValue ? t.Quantity * t.UnitCost.Value : null,
                RecordedBy = t.RecordedByName,
                Source = t.RecordedByName != null ? "Staff" : null,
                Details = t.Notes,
                CreatedAtUtc = t.CreatedAtUtc
            }));
        }

        // Ledger entries
        if (type == null || type == "payment" || type == "adjustment")
        {
            var ledgerRaw = await _db.LedgerEntries
                .Include(e => e.Contact)
                .Where(e => e.BusinessId == businessId)
                .OrderByDescending(e => e.CreatedAtUtc)
                .ToListAsync();

            foreach (var e in ledgerRaw)
            {
                var isAdjustment = e.Source == "Adjustment";
                var contactName = e.Contact.Name;
                string activityType;
                string description;

                if (isAdjustment)
                {
                    activityType = "adjustment";
                    description = e.Notes?.StartsWith("Deleted:") == true
                        ? $"Deleted ledger entry for {contactName}"
                        : $"Adjusted debt for {contactName}";
                }
                else
                {
                    switch (e.EntryType)
                    {
                        case LedgerEntryType.Receivable:
                            activityType = "debt_recorded";
                            description = $"{contactName} owes you {cs}{e.Amount:N0}";
                            break;
                        case LedgerEntryType.ReceivablePayment:
                            activityType = "payment_received";
                            description = $"{contactName} paid {cs}{e.Amount:N0}";
                            break;
                        case LedgerEntryType.Payable:
                            activityType = "debt_recorded";
                            description = $"You owe {contactName} {cs}{e.Amount:N0}";
                            break;
                        case LedgerEntryType.PayablePayment:
                            activityType = "payment_made";
                            description = $"You paid {contactName} {cs}{e.Amount:N0}";
                            break;
                        default:
                            activityType = "ledger";
                            description = e.EntryType.ToString();
                            break;
                    }
                }

                activities.Add(new ActivityFeedDto
                {
                    Id = e.Id,
                    RefId = MakeRef(e.Id, "LDG"),
                    Type = activityType,
                    Description = description,
                    Amount = e.Amount,
                    ContactName = contactName,
                    RecordedBy = e.RecordedByName,
                    Source = e.Source,
                    Details = e.Notes,
                    CreatedAtUtc = e.CreatedAtUtc
                });
            }
        }

        // Apply date range filter
        if (startDate.HasValue)
            activities = activities.Where(a => a.CreatedAtUtc >= startDate.Value).ToList();
        if (endDate.HasValue)
            activities = activities.Where(a => a.CreatedAtUtc < endDate.Value.AddDays(1)).ToList();

        // Apply search filter — searches description, details, contact name, recorded by, ref ID
        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.ToLowerInvariant();
            activities = activities.Where(a =>
                (a.Description?.ToLower().Contains(q) == true) ||
                (a.Details?.ToLower().Contains(q) == true) ||
                (a.ContactName?.ToLower().Contains(q) == true) ||
                (a.RecordedBy?.ToLower().Contains(q) == true) ||
                (a.RefId?.ToLower().Contains(q) == true) ||
                (a.Source?.ToLower().Contains(q) == true)
            ).ToList();
        }

        // Sort and paginate
        var sorted = activities.OrderByDescending(a => a.CreatedAtUtc).ToList();
        var totalCount = sorted.Count;
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        var paged = sorted.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new PaginatedActivityResult
        {
            Items = paged,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalPages
        };
    }

    private async Task<List<TrendPointDto>> BuildSalesTrendAsync(Guid businessId, DateTime from, DateTime to)
    {
        var sales = await _db.Sales
            .Where(s => s.BusinessId == businessId && s.CreatedAtUtc >= from && s.CreatedAtUtc <= to.AddDays(1))
            .GroupBy(s => s.CreatedAtUtc.Date)
            .Select(g => new { Date = g.Key, Amount = g.Sum(s => s.TotalAmount) })
            .ToListAsync();

        return Enumerable.Range(0, 7)
            .Select(i => from.AddDays(i).Date)
            .Select(d => new TrendPointDto
            {
                Date = d.ToString("yyyy-MM-dd"),
                Amount = sales.FirstOrDefault(s => s.Date == d)?.Amount ?? 0
            })
            .ToList();
    }

    private async Task<List<TrendPointDto>> BuildExpenseTrendAsync(Guid businessId, DateTime from, DateTime to)
    {
        var expenses = await _db.Expenses
            .Where(e => e.BusinessId == businessId && e.CreatedAtUtc >= from && e.CreatedAtUtc <= to.AddDays(1))
            .GroupBy(e => e.CreatedAtUtc.Date)
            .Select(g => new { Date = g.Key, Amount = g.Sum(e => e.Amount) })
            .ToListAsync();

        return Enumerable.Range(0, 7)
            .Select(i => from.AddDays(i).Date)
            .Select(d => new TrendPointDto
            {
                Date = d.ToString("yyyy-MM-dd"),
                Amount = expenses.FirstOrDefault(e => e.Date == d)?.Amount ?? 0
            })
            .ToList();
    }

    public async Task<List<DeadStockItemDto>> GetDeadStockAsync(Guid businessId)
    {
        var twoWeeksAgo = DateTime.UtcNow.AddDays(-14);
        var allProducts = await _db.Products
            .Where(p => p.BusinessId == businessId && p.IsActive && p.CurrentStock > 0)
            .ToListAsync();

        var productIds = allProducts.Select(p => p.Id).ToList();
        var recentSoldIds = await _db.SaleItems
            .Include(i => i.Sale)
            .Where(i => i.Sale.BusinessId == businessId && i.Sale.CreatedAtUtc >= twoWeeksAgo && productIds.Contains(i.ProductId))
            .Select(i => i.ProductId)
            .Distinct()
            .ToListAsync();

        var lastSaleDates = await _db.SaleItems
            .Include(i => i.Sale)
            .Where(i => i.Sale.BusinessId == businessId && productIds.Contains(i.ProductId))
            .GroupBy(i => i.ProductId)
            .Select(g => new { ProductId = g.Key, LastSale = g.Max(i => i.Sale.CreatedAtUtc) })
            .ToDictionaryAsync(x => x.ProductId, x => x.LastSale);

        return allProducts
            .Where(p => !recentSoldIds.Contains(p.Id))
            .Select(p => new DeadStockItemDto
            {
                ProductId = p.Id,
                ProductName = p.Name,
                Unit = p.Unit,
                CurrentStock = p.CurrentStock,
                DaysSinceLastSale = lastSaleDates.TryGetValue(p.Id, out var last)
                    ? (int)(DateTime.UtcNow - last).TotalDays
                    : -1
            })
            .OrderByDescending(p => p.DaysSinceLastSale)
            .ToList();
    }

    public async Task<List<StockoutPredictionDto>> GetStockoutPredictionsAsync(Guid businessId)
    {
        var sevenDaysAgo = DateTime.UtcNow.Date.AddDays(-6);
        var products = await _db.Products
            .Where(p => p.BusinessId == businessId && p.IsActive && p.CurrentStock > 0)
            .ToListAsync();

        var productIds = products.Select(p => p.Id).ToList();
        var salesByProduct = await _db.SaleItems
            .Include(i => i.Sale)
            .Where(i => i.Sale.BusinessId == businessId && i.Sale.CreatedAtUtc >= sevenDaysAgo && productIds.Contains(i.ProductId))
            .GroupBy(i => i.ProductId)
            .Select(g => new { ProductId = g.Key, TotalSold = g.Sum(i => i.Quantity) })
            .ToDictionaryAsync(x => x.ProductId, x => x.TotalSold);

        return products
            .Select(p =>
            {
                var sold7d = salesByProduct.GetValueOrDefault(p.Id, 0);
                var dailyRate = sold7d / 7m;
                var daysLeft = dailyRate > 0 ? p.CurrentStock / dailyRate : 999;
                var restock = dailyRate > 0 ? Math.Max(0, (dailyRate * 7) - p.CurrentStock) : 0;
                var urgency = daysLeft <= 3 ? "critical" : daysLeft <= 7 ? "warning" : "ok";
                return new StockoutPredictionDto
                {
                    ProductId = p.Id,
                    ProductName = p.Name,
                    Unit = p.Unit,
                    CurrentStock = p.CurrentStock,
                    DailyRate = Math.Round(dailyRate, 2),
                    DaysLeft = Math.Round(daysLeft, 1),
                    RestockQty = Math.Round(restock, 2),
                    Urgency = urgency
                };
            })
            .Where(p => p.DaysLeft < 14 && p.DailyRate > 0)
            .OrderBy(p => p.DaysLeft)
            .Take(20)
            .ToList();
    }

    public async Task<List<ProductProfitDto>> GetProfitByProductAsync(Guid businessId)
    {
        var thirtyDaysAgo = DateTime.UtcNow.Date.AddDays(-29);
        var saleItems = await _db.SaleItems
            .Include(i => i.Sale)
            .Include(i => i.Product)
            .Where(i => i.Sale.BusinessId == businessId && i.Sale.CreatedAtUtc >= thirtyDaysAgo && i.Product.CostPrice.HasValue)
            .ToListAsync();

        return saleItems
            .GroupBy(i => i.Product.Name)
            .Select(g =>
            {
                var revenue = g.Sum(i => i.TotalPrice);
                var cost = g.Sum(i => i.Quantity * i.Product.CostPrice!.Value);
                var profit = revenue - cost;
                return new ProductProfitDto
                {
                    ProductName = g.Key,
                    Revenue = revenue,
                    Cost = cost,
                    Profit = profit,
                    Margin = revenue > 0 ? Math.Round(profit / revenue * 100, 1) : 0
                };
            })
            .OrderByDescending(p => p.Profit)
            .ToList();
    }

    public async Task<List<StaffSalesDto>> GetStaffSalesAsync(Guid businessId, string? staffName, DateOnly? date)
    {
        var targetDate = date?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) ?? DateTime.UtcNow.Date;

        var query = _db.SaleItems
            .Include(i => i.Sale)
            .Include(i => i.Product)
            .Where(i => i.Sale.BusinessId == businessId
                        && i.Sale.CreatedAtUtc >= targetDate
                        && i.Sale.CreatedAtUtc < targetDate.AddDays(1)
                        && i.Sale.RecordedByName != null);

        if (!string.IsNullOrEmpty(staffName))
            query = query.Where(i => i.Sale.RecordedByName!.ToLower().Contains(staffName.ToLower()));

        var saleItems = await query.ToListAsync();

        return saleItems
            .GroupBy(i => i.Sale.RecordedByName!)
            .Select(staffGroup => new StaffSalesDto
            {
                StaffName = staffGroup.Key,
                TotalRevenue = staffGroup.Sum(i => i.TotalPrice),
                SaleCount = staffGroup.Select(i => i.SaleId).Distinct().Count(),
                Items = staffGroup
                    .GroupBy(i => new { i.Product.Name, i.Product.Unit })
                    .Select(g => new StaffSaleItemDto
                    {
                        ProductName = g.Key.Name,
                        Unit = g.Key.Unit,
                        Quantity = g.Sum(i => i.Quantity),
                        Revenue = g.Sum(i => i.TotalPrice)
                    })
                    .OrderByDescending(i => i.Revenue)
                    .ToList()
            })
            .OrderByDescending(s => s.TotalRevenue)
            .ToList();
    }

    private static DateOnly GetMondayOfCurrentWeek()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var daysFromMonday = ((int)today.DayOfWeek - 1 + 7) % 7;
        return today.AddDays(-daysFromMonday);
    }
}
