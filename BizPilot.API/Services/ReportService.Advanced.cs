using BizPilot.API.DTOs.Reports;
using BizPilot.API.Models;
using Microsoft.EntityFrameworkCore;

namespace BizPilot.API.Services;

/// <summary>
/// Advanced reports for paid-tier customers. All methods here back endpoints gated on the
/// <c>advanced_reports</c> feature flag at the controller layer. Kept in a partial file to keep the
/// baseline <see cref="ReportService"/> readable.
///
/// Conventions used throughout:
///   - "Month" strings are formatted as yyyy-MM-01 for machine-sortable display.
///   - Aging buckets use UTC "now" as the reference date.
///   - Sales use <c>IgnoreQueryFilters</c> only when we specifically need to include voided sales.
///   - All aggregations run in-memory after a filtered DB pull to avoid LINQ-to-SQL translation
///     limitations with Postgres for things like week-of-year and hour-of-day groupings.
/// </summary>
public partial class ReportService
{
    private static readonly TimeZoneInfo LagosZone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos");

    // ── Aging reports ────────────────────────────────────────────────────────

    public Task<AgingReportDto> GetAgingReceivablesAsync(Guid businessId)
        => BuildAgingReportAsync(businessId, LedgerEntryType.Receivable, LedgerEntryType.ReceivablePayment);

    public Task<AgingReportDto> GetAgingPayablesAsync(Guid businessId)
        => BuildAgingReportAsync(businessId, LedgerEntryType.Payable, LedgerEntryType.PayablePayment);

    private async Task<AgingReportDto> BuildAgingReportAsync(Guid businessId, LedgerEntryType debtType, LedgerEntryType paymentType)
    {
        var now = DateTime.UtcNow;

        var entries = await _db.LedgerEntries
            .Include(e => e.Contact)
            .Where(e => e.BusinessId == businessId && (e.EntryType == debtType || e.EntryType == paymentType))
            .ToListAsync();

        var report = new AgingReportDto();
        var byContact = entries.GroupBy(e => new { e.ContactId, ContactName = e.Contact.Name });

        foreach (var group in byContact)
        {
            var debts = group.Where(e => e.EntryType == debtType).OrderBy(e => e.CreatedAtUtc).ToList();
            var totalPaid = group.Where(e => e.EntryType == paymentType).Sum(e => e.Amount);

            // Apply payments to oldest debts first (FIFO) — this is the standard aging convention so the
            // remaining unpaid amount is biased toward the newest invoices.
            var remainingPayments = totalPaid;
            var bucket = new AgingContactDto { ContactId = group.Key.ContactId, ContactName = group.Key.ContactName };
            int oldestDays = 0;

            foreach (var debt in debts)
            {
                var stillOwed = debt.Amount;
                if (remainingPayments > 0)
                {
                    var applied = Math.Min(remainingPayments, stillOwed);
                    stillOwed -= applied;
                    remainingPayments -= applied;
                }
                if (stillOwed <= 0) continue;

                var ageDays = (int)(now - debt.CreatedAtUtc).TotalDays;
                if (ageDays > oldestDays) oldestDays = ageDays;

                if (ageDays <= 30) bucket.Bucket0To30 += stillOwed;
                else if (ageDays <= 60) bucket.Bucket31To60 += stillOwed;
                else if (ageDays <= 90) bucket.Bucket61To90 += stillOwed;
                else bucket.Bucket90Plus += stillOwed;
            }

            bucket.Total = bucket.Bucket0To30 + bucket.Bucket31To60 + bucket.Bucket61To90 + bucket.Bucket90Plus;
            bucket.OldestDays = oldestDays;
            if (bucket.Total > 0) report.Contacts.Add(bucket);
        }

        report.Contacts = report.Contacts.OrderByDescending(c => c.Bucket90Plus).ThenByDescending(c => c.Total).ToList();
        report.Total0To30 = report.Contacts.Sum(c => c.Bucket0To30);
        report.Total31To60 = report.Contacts.Sum(c => c.Bucket31To60);
        report.Total61To90 = report.Contacts.Sum(c => c.Bucket61To90);
        report.Total90Plus = report.Contacts.Sum(c => c.Bucket90Plus);
        report.GrandTotal = report.Total0To30 + report.Total31To60 + report.Total61To90 + report.Total90Plus;
        return report;
    }

    // ── Monthly P&L ──────────────────────────────────────────────────────────

    public async Task<MonthlyPnlDto> GetMonthlyPnlAsync(Guid businessId, DateOnly? month)
    {
        var anchor = month ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var thisMonthStart = new DateTime(anchor.Year, anchor.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var nextMonthStart = thisMonthStart.AddMonths(1);
        var prevMonthStart = thisMonthStart.AddMonths(-1);

        var (currentRev, currentCogs, _) = await ComputePnlSegmentAsync(businessId, thisMonthStart, nextMonthStart);
        var currentOpex = await _db.Expenses
            .Where(e => e.BusinessId == businessId && e.CreatedAtUtc >= thisMonthStart && e.CreatedAtUtc < nextMonthStart
                        && e.Category != "Inventory")
            .SumAsync(e => e.Amount);

        var (prevRev, prevCogs, _) = await ComputePnlSegmentAsync(businessId, prevMonthStart, thisMonthStart);
        var prevOpex = await _db.Expenses
            .Where(e => e.BusinessId == businessId && e.CreatedAtUtc >= prevMonthStart && e.CreatedAtUtc < thisMonthStart
                        && e.Category != "Inventory")
            .SumAsync(e => e.Amount);

        var grossProfit = currentRev - currentCogs;
        var netProfit = grossProfit - currentOpex;
        var prevGross = prevRev - prevCogs;
        var prevNet = prevGross - prevOpex;

        return new MonthlyPnlDto
        {
            Month = thisMonthStart.ToString("yyyy-MM-01"),
            PreviousMonth = prevMonthStart.ToString("yyyy-MM-01"),
            Revenue = currentRev,
            PreviousRevenue = prevRev,
            CostOfGoodsSold = currentCogs,
            PreviousCostOfGoodsSold = prevCogs,
            GrossProfit = grossProfit,
            PreviousGrossProfit = prevGross,
            OperatingExpenses = currentOpex,
            PreviousOperatingExpenses = prevOpex,
            NetProfit = netProfit,
            PreviousNetProfit = prevNet,
            GrossMarginPercent = currentRev > 0 ? Math.Round(grossProfit / currentRev * 100, 2) : 0,
            NetMarginPercent = currentRev > 0 ? Math.Round(netProfit / currentRev * 100, 2) : 0,
            // COGS is an estimate when product cost prices weren't set at time of sale — we fall back to the
            // current cost price, which may drift from the historical cost. Flagged so the UI can disclaim it.
            IsEstimate = true
        };
    }

    private async Task<(decimal Revenue, decimal Cogs, int Count)> ComputePnlSegmentAsync(Guid businessId, DateTime startUtc, DateTime endUtc)
    {
        var saleItems = await _db.SaleItems
            .Include(si => si.Product)
            .Where(si => si.Sale.BusinessId == businessId
                         && si.Sale.CreatedAtUtc >= startUtc
                         && si.Sale.CreatedAtUtc < endUtc)
            .ToListAsync();

        var revenue = saleItems.Sum(si => si.TotalPrice);
        var cogs = saleItems.Where(si => si.Product.CostPrice.HasValue)
                            .Sum(si => si.Quantity * si.Product.CostPrice!.Value);
        var count = saleItems.Select(si => si.SaleId).Distinct().Count();
        return (revenue, cogs, count);
    }

    // ── Expense breakdown ────────────────────────────────────────────────────

    public async Task<ExpenseBreakdownDto> GetExpenseBreakdownAsync(Guid businessId, DateOnly? month)
    {
        var anchor = month ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var start = new DateTime(anchor.Year, anchor.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddMonths(1);

        var expenses = await _db.Expenses
            .Where(e => e.BusinessId == businessId && e.CreatedAtUtc >= start && e.CreatedAtUtc < end)
            .ToListAsync();

        var total = expenses.Sum(e => e.Amount);
        var breakdown = new ExpenseBreakdownDto
        {
            Month = start.ToString("yyyy-MM-01"),
            TotalExpenses = total
        };

        var grouped = expenses
            .GroupBy(e => e.Category)
            .Select(g => new ExpenseCategoryDto
            {
                Category = g.Key,
                Amount = g.Sum(e => e.Amount),
                EntryCount = g.Count(),
                PercentOfTotal = total > 0 ? Math.Round(g.Sum(e => e.Amount) / total * 100, 2) : 0
            })
            .OrderByDescending(c => c.Amount)
            .ToList();

        breakdown.Categories = grouped;
        return breakdown;
    }

    // ── Inventory turnover ───────────────────────────────────────────────────

    public async Task<List<InventoryTurnoverDto>> GetInventoryTurnoverAsync(Guid businessId)
    {
        var cutoff = DateTime.UtcNow.AddDays(-30);

        var products = await _db.Products
            .Where(p => p.BusinessId == businessId && p.IsActive)
            .ToListAsync();

        var salesByProduct = await _db.SaleItems
            .Where(si => si.Sale.BusinessId == businessId && si.Sale.CreatedAtUtc >= cutoff)
            .GroupBy(si => si.ProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                TotalQty = g.Sum(si => si.Quantity),
                TotalCogs = g.Sum(si => si.Product.CostPrice.HasValue ? si.Quantity * si.Product.CostPrice.Value : 0m)
            })
            .ToListAsync();

        var lookup = salesByProduct.ToDictionary(x => x.ProductId);

        var result = new List<InventoryTurnoverDto>();
        foreach (var p in products)
        {
            lookup.TryGetValue(p.Id, out var sold);
            var qtySold = sold?.TotalQty ?? 0;
            var cogs = sold?.TotalCogs ?? 0;
            var velocity = qtySold / 30m;
            var daysRemaining = velocity > 0 ? p.CurrentStock / velocity : decimal.MaxValue;

            var avgInventory = (p.CurrentStock + qtySold) / 2m;
            var inventoryValue = (p.CostPrice ?? 0) * p.CurrentStock;
            var avgInventoryValue = avgInventory * (p.CostPrice ?? 0);
            var turnoverRatio = avgInventoryValue > 0
                ? cogs / avgInventoryValue
                : 0m;

            // Thresholds picked to match small-retailer expectations; tune later if we learn what feels right.
            var classification = qtySold == 0 ? "Dead"
                : velocity >= 1m ? "Fast"
                : velocity >= 0.2m ? "Healthy"
                : "Slow";

            result.Add(new InventoryTurnoverDto
            {
                ProductId = p.Id,
                ProductName = p.Name,
                Unit = p.Unit,
                CurrentStock = p.CurrentStock,
                SoldLast30Days = qtySold,
                DailyVelocity = Math.Round(velocity, 3),
                DaysOfStockRemaining = daysRemaining > 999 ? 999 : Math.Round(daysRemaining, 1),
                CostOfGoodsSold = cogs,
                InventoryValue = inventoryValue,
                TurnoverRatio = Math.Round(turnoverRatio, 2),
                Classification = classification
            });
        }

        return result.OrderBy(r => r.Classification == "Dead" ? 0 : r.Classification == "Slow" ? 1 : 2)
                     .ThenByDescending(r => r.InventoryValue)
                     .ToList();
    }

    // ── Top customers + concentration ────────────────────────────────────────

    public async Task<TopCustomersReportDto> GetTopCustomersAsync(Guid businessId, int limit)
    {
        limit = Math.Clamp(limit, 1, 100);

        var cutoff = DateTime.UtcNow.AddMonths(-12);

        var allSales = await _db.Sales
            .Include(s => s.Contact)
            .Where(s => s.BusinessId == businessId && s.CreatedAtUtc >= cutoff)
            .ToListAsync();

        var total = allSales.Sum(s => s.TotalAmount);

        var grouped = allSales
            .Where(s => s.ContactId.HasValue)
            .GroupBy(s => new { s.ContactId, ContactName = s.Contact!.Name })
            .Select(g => new TopCustomerDetailDto
            {
                ContactId = g.Key.ContactId!.Value,
                ContactName = g.Key.ContactName,
                TotalRevenue = g.Sum(s => s.TotalAmount),
                TransactionCount = g.Count(),
                LastPurchaseAtUtc = g.Max(s => s.CreatedAtUtc),
                PercentOfTotal = total > 0 ? Math.Round(g.Sum(s => s.TotalAmount) / total * 100, 2) : 0
            })
            .OrderByDescending(c => c.TotalRevenue)
            .Take(limit)
            .ToList();

        var topPct = grouped.FirstOrDefault()?.PercentOfTotal ?? 0;
        return new TopCustomersReportDto
        {
            TotalRevenue = total,
            TopCustomerPercent = topPct,
            ConcentrationRisk = topPct >= 40,
            Customers = grouped
        };
    }

    // ── Sales heatmap ────────────────────────────────────────────────────────

    public async Task<SalesHeatmapDto> GetSalesHeatmapAsync(Guid businessId, int weeks, string? timezone = null)
    {
        weeks = Math.Clamp(weeks, 1, 52);
        var cutoff = DateTime.UtcNow.AddDays(-weeks * 7);

        var sales = await _db.Sales
            .Where(s => s.BusinessId == businessId && s.CreatedAtUtc >= cutoff)
            .Select(s => new { s.CreatedAtUtc, s.TotalAmount })
            .ToListAsync();

        // Group in the business's local timezone so "peak at 6pm" matches what the owner actually experiences.
        var tz = LagosZone;
        if (!string.IsNullOrEmpty(timezone))
        {
            try { tz = TimeZoneInfo.FindSystemTimeZoneById(timezone); }
            catch { /* fall back to Lagos if invalid */ }
        }

        var cells = new Dictionary<(int Day, int Hour), (decimal Revenue, int Count)>();
        foreach (var s in sales)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(s.CreatedAtUtc, DateTimeKind.Utc), tz);
            var key = ((int)local.DayOfWeek, local.Hour);
            var entry = cells.GetValueOrDefault(key);
            cells[key] = (entry.Revenue + s.TotalAmount, entry.Count + 1);
        }

        var list = cells.Select(kv => new SalesHeatmapCellDto
        {
            DayOfWeek = kv.Key.Day,
            Hour = kv.Key.Hour,
            Revenue = kv.Value.Revenue,
            SaleCount = kv.Value.Count
        }).OrderBy(c => c.DayOfWeek).ThenBy(c => c.Hour).ToList();

        var peak = list.OrderByDescending(c => c.Revenue).FirstOrDefault();
        return new SalesHeatmapDto
        {
            WeeksAnalyzed = weeks,
            PeakRevenue = peak?.Revenue ?? 0,
            PeakDayOfWeek = peak?.DayOfWeek ?? 0,
            PeakHour = peak?.Hour ?? 0,
            Cells = list
        };
    }

    // ── Month-over-month trend ───────────────────────────────────────────────

    public async Task<MonthlyTrendDto> GetMonthlyTrendAsync(Guid businessId, int months)
    {
        months = Math.Clamp(months, 1, 24);
        var now = DateTime.UtcNow;
        var earliest = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-(months - 1));

        var sales = await _db.Sales
            .Where(s => s.BusinessId == businessId && s.CreatedAtUtc >= earliest)
            .Select(s => new { s.CreatedAtUtc, s.TotalAmount })
            .ToListAsync();

        var expenses = await _db.Expenses
            .Where(e => e.BusinessId == businessId && e.CreatedAtUtc >= earliest)
            .Select(e => new { e.CreatedAtUtc, e.Amount })
            .ToListAsync();

        var points = new List<MonthlyTrendPointDto>();
        for (int i = 0; i < months; i++)
        {
            var monthStart = earliest.AddMonths(i);
            var monthEnd = monthStart.AddMonths(1);

            var monthSales = sales.Where(s => s.CreatedAtUtc >= monthStart && s.CreatedAtUtc < monthEnd).ToList();
            var monthExp = expenses.Where(e => e.CreatedAtUtc >= monthStart && e.CreatedAtUtc < monthEnd).Sum(e => e.Amount);
            var revenue = monthSales.Sum(s => s.TotalAmount);

            points.Add(new MonthlyTrendPointDto
            {
                Month = monthStart.ToString("yyyy-MM-01"),
                Revenue = revenue,
                Expenses = monthExp,
                Profit = revenue - monthExp,
                TransactionCount = monthSales.Count
            });
        }

        return new MonthlyTrendDto { Points = points };
    }

    // ── Payment method split ─────────────────────────────────────────────────

    public async Task<PaymentMethodSplitDto> GetPaymentMethodSplitAsync(Guid businessId, int months)
    {
        months = Math.Clamp(months, 1, 24);
        var now = DateTime.UtcNow;
        var earliest = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-(months - 1));

        var sales = await _db.Sales
            .Where(s => s.BusinessId == businessId && s.CreatedAtUtc >= earliest)
            .Select(s => new { s.CreatedAtUtc, s.TotalAmount, s.PaymentMethod, s.PaymentStatus })
            .ToListAsync();

        var result = new PaymentMethodSplitDto();
        for (int i = 0; i < months; i++)
        {
            var monthStart = earliest.AddMonths(i);
            var monthEnd = monthStart.AddMonths(1);
            var monthSales = sales.Where(s => s.CreatedAtUtc >= monthStart && s.CreatedAtUtc < monthEnd).ToList();

            var pt = new PaymentMethodMonthDto { Month = monthStart.ToString("yyyy-MM-01") };
            foreach (var s in monthSales)
            {
                // Unpaid/credit sales go into the credit bucket regardless of the PaymentMethod field.
                if (s.PaymentStatus != PaymentStatus.Paid) { pt.Credit += s.TotalAmount; continue; }
                var bucket = ClassifyMethod(s.PaymentMethod);
                switch (bucket)
                {
                    case "cash": pt.Cash += s.TotalAmount; break;
                    case "transfer": pt.Transfer += s.TotalAmount; break;
                    case "pos": pt.Pos += s.TotalAmount; break;
                    default: pt.Other += s.TotalAmount; break;
                }
            }
            result.Months.Add(pt);
        }

        result.TotalCash = result.Months.Sum(m => m.Cash);
        result.TotalTransfer = result.Months.Sum(m => m.Transfer);
        result.TotalPos = result.Months.Sum(m => m.Pos);
        result.TotalCredit = result.Months.Sum(m => m.Credit);
        result.TotalOther = result.Months.Sum(m => m.Other);
        return result;
    }

    private static string ClassifyMethod(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "other";
        var lower = raw.Trim().ToLowerInvariant();
        return lower switch
        {
            "cash" => "cash",
            "bank transfer" or "transfer" or "bank" or "wire" or "wire transfer" or "bank deposit" => "transfer",
            "pos" or "card" or "debit card" or "credit card" or "pos machine" => "pos",
            _ => lower.Contains("transfer") || lower.Contains("bank") ? "transfer"
               : lower.Contains("cash") ? "cash"
               : lower.Contains("pos") || lower.Contains("card") ? "pos"
               : "other"
        };
    }

    // ── Customer payment reliability ─────────────────────────────────────────

    public async Task<List<CustomerReliabilityDto>> GetCustomerReliabilityAsync(Guid businessId)
    {
        var entries = await _db.LedgerEntries
            .Include(e => e.Contact)
            .Where(e => e.BusinessId == businessId
                        && (e.EntryType == LedgerEntryType.Receivable || e.EntryType == LedgerEntryType.ReceivablePayment))
            .OrderBy(e => e.CreatedAtUtc)
            .ToListAsync();

        var byContact = entries.GroupBy(e => new { e.ContactId, ContactName = e.Contact.Name });
        var result = new List<CustomerReliabilityDto>();

        foreach (var group in byContact)
        {
            var receivables = new Queue<LedgerEntry>(group.Where(e => e.EntryType == LedgerEntryType.Receivable).OrderBy(e => e.CreatedAtUtc));
            var payments = group.Where(e => e.EntryType == LedgerEntryType.ReceivablePayment).OrderBy(e => e.CreatedAtUtc).ToList();

            // Pair each payment (FIFO) against the oldest open receivable to estimate days-to-pay. This is an
            // approximation since we don't track allocation at write time, but it's the standard accounting
            // approach and accurate enough for ranking customers.
            var days = new List<double>();
            decimal totalPaid = 0;
            foreach (var payment in payments)
            {
                var remainingPayment = payment.Amount;
                while (remainingPayment > 0 && receivables.Count > 0)
                {
                    var head = receivables.Peek();
                    var applied = Math.Min(remainingPayment, head.Amount);
                    var elapsed = (payment.CreatedAtUtc - head.CreatedAtUtc).TotalDays;
                    if (elapsed >= 0) days.Add(elapsed);
                    remainingPayment -= applied;
                    totalPaid += applied;

                    if (applied >= head.Amount) receivables.Dequeue();
                    else
                    {
                        head.Amount -= applied;
                        break;
                    }
                }
            }

            if (days.Count == 0)
            {
                var hasOutstanding = receivables.Count > 0;
                if (hasOutstanding)
                {
                    result.Add(new CustomerReliabilityDto
                    {
                        ContactId = group.Key.ContactId,
                        ContactName = group.Key.ContactName,
                        PaidReceivables = 0,
                        AverageDaysToPay = 0,
                        TotalPaid = 0,
                        Classification = "Unknown"
                    });
                }
                continue;
            }
            var avg = (decimal)days.Average();
            var classification = avg <= 7 ? "Prompt"
                : avg <= 21 ? "Regular"
                : avg <= 45 ? "Slow"
                : "Late";

            result.Add(new CustomerReliabilityDto
            {
                ContactId = group.Key.ContactId,
                ContactName = group.Key.ContactName,
                PaidReceivables = days.Count,
                AverageDaysToPay = Math.Round(avg, 1),
                TotalPaid = totalPaid,
                Classification = classification
            });
        }

        return result.OrderByDescending(r => r.TotalPaid).ToList();
    }

    // ── Wastage / damage report ──────────────────────────────────────────────

    public async Task<WastageReportDto> GetWastageReportAsync(Guid businessId, int days)
    {
        days = Math.Clamp(days, 1, 365);
        var cutoff = DateTime.UtcNow.AddDays(-days);

        var damage = await _db.InventoryTransactions
            .Include(t => t.Product)
            .Where(t => t.BusinessId == businessId
                        && (t.Type == InventoryTransactionType.Damaged || t.Type == InventoryTransactionType.Wastage)
                        && t.CreatedAtUtc >= cutoff)
            .ToListAsync();

        decimal TotalLoss(InventoryTransaction t)
            => t.Quantity * (t.UnitCost ?? t.Product.CostPrice ?? 0);

        var grouped = damage
            .GroupBy(t => new { t.ProductId, t.Product.Name, t.Product.Unit })
            .Select(g => new WastageItemDto
            {
                ProductId = g.Key.ProductId,
                ProductName = g.Key.Name,
                Unit = g.Key.Unit,
                QuantityDamaged = g.Sum(t => t.Quantity),
                EstimatedLoss = g.Sum(TotalLoss),
                EventCount = g.Count()
            })
            .OrderByDescending(w => w.EstimatedLoss)
            .Take(25)
            .ToList();

        return new WastageReportDto
        {
            Period = $"Last {days} days",
            TotalValue = damage.Sum(TotalLoss),
            EventCount = damage.Count,
            TopProducts = grouped
        };
    }

    // ── Average transaction value ────────────────────────────────────────────

    public async Task<AvgTransactionValueDto> GetAvgTransactionValueAsync(Guid businessId, int months)
    {
        months = Math.Clamp(months, 1, 24);
        var earliest = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-(months - 1));

        var sales = await _db.Sales
            .Where(s => s.BusinessId == businessId && s.CreatedAtUtc >= earliest)
            .Select(s => new { s.CreatedAtUtc, s.TotalAmount })
            .ToListAsync();

        var points = new List<AvgTransactionPointDto>();
        for (int i = 0; i < months; i++)
        {
            var monthStart = earliest.AddMonths(i);
            var monthEnd = monthStart.AddMonths(1);
            var chunk = sales.Where(s => s.CreatedAtUtc >= monthStart && s.CreatedAtUtc < monthEnd).ToList();
            var avg = chunk.Count > 0 ? chunk.Sum(s => s.TotalAmount) / chunk.Count : 0m;

            points.Add(new AvgTransactionPointDto
            {
                Month = monthStart.ToString("yyyy-MM-01"),
                AverageValue = Math.Round(avg, 2),
                TransactionCount = chunk.Count
            });
        }

        return new AvgTransactionValueDto { Points = points };
    }

    // ── Customer retention ───────────────────────────────────────────────────

    public async Task<CustomerRetentionDto> GetCustomerRetentionAsync(Guid businessId, int months)
    {
        months = Math.Clamp(months, 1, 12);
        var earliest = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-(months - 1));

        // Grab first-purchase dates per contact to classify "new" vs "returning" for each month in the window.
        var allSales = await _db.Sales
            .Where(s => s.BusinessId == businessId && s.ContactId.HasValue)
            .Select(s => new { s.ContactId, s.CreatedAtUtc, s.TotalAmount })
            .ToListAsync();

        var firstPurchase = allSales.GroupBy(s => s.ContactId!.Value)
            .ToDictionary(g => g.Key, g => g.Min(s => s.CreatedAtUtc));

        var result = new CustomerRetentionDto();
        for (int i = 0; i < months; i++)
        {
            var monthStart = earliest.AddMonths(i);
            var monthEnd = monthStart.AddMonths(1);
            var monthSales = allSales.Where(s => s.CreatedAtUtc >= monthStart && s.CreatedAtUtc < monthEnd).ToList();

            var newCustomers = monthSales.Where(s => firstPurchase[s.ContactId!.Value] >= monthStart
                                                    && firstPurchase[s.ContactId.Value] < monthEnd).ToList();
            var returning = monthSales.Where(s => firstPurchase[s.ContactId!.Value] < monthStart).ToList();

            result.Months.Add(new RetentionMonthDto
            {
                Month = monthStart.ToString("yyyy-MM-01"),
                NewCustomers = newCustomers.Select(s => s.ContactId).Distinct().Count(),
                ReturningCustomers = returning.Select(s => s.ContactId).Distinct().Count(),
                NewRevenue = newCustomers.Sum(s => s.TotalAmount),
                ReturningRevenue = returning.Sum(s => s.TotalAmount)
            });
        }

        return result;
    }

    // ── Reorder suggestions ──────────────────────────────────────────────────

    public async Task<List<ReorderSuggestionDto>> GetReorderSuggestionsAsync(Guid businessId, int safetyDays)
    {
        safetyDays = Math.Clamp(safetyDays, 1, 90);
        var cutoff = DateTime.UtcNow.AddDays(-30);

        var products = await _db.Products
            .Where(p => p.BusinessId == businessId && p.IsActive)
            .ToListAsync();

        var velocity = await _db.SaleItems
            .Where(si => si.Sale.BusinessId == businessId && si.Sale.CreatedAtUtc >= cutoff)
            .GroupBy(si => si.ProductId)
            .Select(g => new { ProductId = g.Key, Qty = g.Sum(si => si.Quantity) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Qty / 30m);

        var suggestions = new List<ReorderSuggestionDto>();
        foreach (var p in products)
        {
            var daily = velocity.GetValueOrDefault(p.Id, 0);
            if (daily <= 0) continue;

            var daysLeft = p.CurrentStock / daily;
            if (daysLeft > safetyDays) continue;

            var target = daily * safetyDays;
            var reorderQty = Math.Max(0, target - p.CurrentStock);
            if (reorderQty <= 0) continue;

            var urgency = daysLeft < 3 ? "Critical"
                : daysLeft < safetyDays / 2m ? "High"
                : "Normal";

            suggestions.Add(new ReorderSuggestionDto
            {
                ProductId = p.Id,
                ProductName = p.Name,
                Unit = p.Unit,
                CurrentStock = p.CurrentStock,
                DailyVelocity = Math.Round(daily, 3),
                SuggestedReorderQty = Math.Ceiling(reorderQty),
                EstimatedCost = Math.Ceiling(reorderQty) * (p.CostPrice ?? 0),
                Urgency = urgency
            });
        }

        return suggestions.OrderBy(s => s.Urgency == "Critical" ? 0 : s.Urgency == "High" ? 1 : 2)
                          .ThenByDescending(s => s.EstimatedCost)
                          .ToList();
    }

    // ── Product affinity (basket analysis) ───────────────────────────────────

    public async Task<List<ProductAffinityDto>> GetProductAffinityAsync(Guid businessId, int limit)
    {
        limit = Math.Clamp(limit, 1, 50);
        var cutoff = DateTime.UtcNow.AddDays(-90);

        // Multi-item sales only — single-item sales contribute no pairs.
        var multiItemSales = await _db.Sales
            .Include(s => s.Items).ThenInclude(i => i.Product)
            .Where(s => s.BusinessId == businessId && s.CreatedAtUtc >= cutoff && s.Items.Count > 1)
            .ToListAsync();

        var pairs = new Dictionary<(Guid, Guid), (int Count, decimal Revenue, string NameA, string NameB)>();
        foreach (var sale in multiItemSales)
        {
            var items = sale.Items.OrderBy(i => i.ProductId).ToList();
            for (int i = 0; i < items.Count; i++)
            {
                for (int j = i + 1; j < items.Count; j++)
                {
                    var a = items[i];
                    var b = items[j];
                    var key = (a.ProductId, b.ProductId);
                    var existing = pairs.GetValueOrDefault(key);
                    pairs[key] = (
                        existing.Count + 1,
                        existing.Revenue + a.TotalPrice + b.TotalPrice,
                        a.Product.Name,
                        b.Product.Name
                    );
                }
            }
        }

        return pairs
            .OrderByDescending(kv => kv.Value.Count)
            .ThenByDescending(kv => kv.Value.Revenue)
            .Take(limit)
            .Select(kv => new ProductAffinityDto
            {
                ProductA = kv.Value.NameA,
                ProductB = kv.Value.NameB,
                CoOccurrenceCount = kv.Value.Count,
                CombinedRevenue = kv.Value.Revenue
            })
            .ToList();
    }

    // ── Weekly Sales Velocity ───────────────────────────────────────────────

    public async Task<WeeklySalesTrendDto> GetWeeklySalesTrendAsync(Guid businessId, int months)
    {
        var cutoff = DateTime.UtcNow.AddMonths(-months);
        var sales = await _db.Sales
            .Where(s => s.BusinessId == businessId && s.CreatedAtUtc >= cutoff)
            .Select(s => new { s.TotalAmount, s.CreatedAtUtc })
            .ToListAsync();

        // Group into ISO weeks (Monday-start)
        var grouped = sales
            .GroupBy(s =>
            {
                var d = s.CreatedAtUtc.Date;
                var daysToMonday = ((int)d.DayOfWeek + 6) % 7;
                return d.AddDays(-daysToMonday);
            })
            .OrderBy(g => g.Key)
            .ToList();

        var weeks = new List<WeeklySalesPointDto>();
        foreach (var g in grouped)
        {
            var weekStart = g.Key;
            var weekEnd = weekStart.AddDays(6);
            var revenue = g.Sum(s => s.TotalAmount);
            var count = g.Count();

            weeks.Add(new WeeklySalesPointDto
            {
                WeekStart = weekStart.ToString("yyyy-MM-dd"),
                WeekEnd = weekEnd.ToString("yyyy-MM-dd"),
                Label = $"{weekStart:MMM d}–{weekEnd:MMM d}",
                Revenue = revenue,
                SaleCount = count,
                AvgOrderValue = count > 0 ? Math.Round(revenue / count, 2) : 0
            });
        }

        // Week-over-week growth
        for (int i = 1; i < weeks.Count; i++)
        {
            var prev = weeks[i - 1].Revenue;
            if (prev > 0)
                weeks[i].GrowthPercent = Math.Round((weeks[i].Revenue - prev) / prev * 100, 1);
        }

        // 4-week moving average
        for (int i = 0; i < weeks.Count; i++)
        {
            var windowStart = Math.Max(0, i - 3);
            var window = weeks.Skip(windowStart).Take(i - windowStart + 1).ToList();
            weeks[i].MovingAvg = Math.Round(window.Average(w => w.Revenue), 2);
        }

        var bestWeek = weeks.OrderByDescending(w => w.Revenue).FirstOrDefault();
        var worstWeek = weeks.Where(w => w.Revenue > 0).OrderBy(w => w.Revenue).FirstOrDefault()
                        ?? weeks.FirstOrDefault();
        var growths = weeks.Where(w => w.GrowthPercent.HasValue).Select(w => w.GrowthPercent!.Value).ToList();

        return new WeeklySalesTrendDto
        {
            Weeks = weeks,
            AvgWeeklyRevenue = weeks.Count > 0 ? Math.Round(weeks.Average(w => w.Revenue), 2) : 0,
            BestWeekRevenue = bestWeek?.Revenue ?? 0,
            BestWeekLabel = bestWeek?.Label ?? "",
            WorstWeekRevenue = worstWeek?.Revenue ?? 0,
            WorstWeekLabel = worstWeek?.Label ?? "",
            AvgGrowthPercent = growths.Count > 0 ? Math.Round(growths.Average(), 1) : 0,
            TotalWeeks = weeks.Count,
            TotalRevenue = weeks.Sum(w => w.Revenue),
            TotalSales = weeks.Sum(w => w.SaleCount)
        };
    }
}
