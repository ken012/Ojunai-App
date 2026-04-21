export interface ApiResponse<T> {
  success: boolean;
  message?: string;
  data?: T;
  errors?: string[];
}

export interface PaginatedResult<T> {
  items: T[];
  totalCount: number;
  totalAmount?: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface AuthResponse {
  token?: string;
  expiresAt: string;
  mustChangePassword?: boolean;
  user: UserDto;
  business: BusinessDto;
}

export interface UserDto {
  id: string;
  fullName: string;
  phoneNumber: string;
  email?: string;
  role: string;
}

export interface BusinessDto {
  id: string;
  name: string;
  businessType?: string;
  currency: string;
  state?: string;
  city?: string;
  country?: string;
  timezone?: string;
  largeSaleThreshold?: number;
  customCategories?: string[];
  alertLowStock?: boolean;
  alertDailySummary?: boolean;
  alertLargeSale?: boolean;
  confirmLargeSales?: boolean;
  confirmLargeSaleThreshold?: number;
  plan?: string;
  subscribedPlan?: string;
  trialEndsAt?: string;
  isActive: boolean;
}

export interface ProductDto {
  id: string;
  name: string;
  sku?: string;
  unit: string;
  costPrice?: number;
  sellingPrice?: number;
  currentStock: number;
  lowStockThreshold: number;
  isLowStock: boolean;
  isActive: boolean;
  category?: string;
  subcategory?: string;
  source?: string;
  recordedByName?: string;
  createdAtUtc: string;
}

export interface SaleSummaryDto {
  id: string;
  totalAmount: number;
  paymentStatus: string;
  paymentMethod?: string;
  itemCount: number;
  itemSummary?: string;
  customerName?: string;
  recordedByName?: string;
  source?: string;
  createdAtUtc: string;
  deletedAtUtc?: string;
}

export interface SaleDto extends SaleSummaryDto {
  paymentMethod?: string;
  notes?: string;
  items: SaleItemDto[];
  contactBalance?: number | null;
  dueDate?: string | null;
}

export interface SaleItemDto {
  productId: string;
  productName: string;
  unit: string;
  quantity: number;
  unitPrice: number;
  totalPrice: number;
}

export interface ExpenseDto {
  id: string;
  category: string;
  expenseType: string;
  amount: number;
  notes?: string;
  paidTo?: string;
  source?: string;
  createdAtUtc: string;
}

export interface ContactDto {
  id: string;
  name: string;
  phoneNumber?: string;
  type: string;
  outstandingReceivable: number;
  outstandingPayable: number;
  createdAtUtc: string;
}

export interface LedgerEntryDto {
  id: string;
  contactName: string;
  entryType: string;
  amount: number;
  notes?: string;
  dueDate?: string;
  source: string;
  createdAtUtc: string;
}

export interface OutstandingBalanceDto {
  contactId: string;
  contactName: string;
  contactType: string;
  totalReceivable: number;
  totalPayable: number;
  netBalance: number;
  recentNotes: string[];
}

export interface DashboardOverviewDto {
  todaySales: number;
  todaySaleCount: number;
  todayExpenses: number;
  outstandingReceivables: number;
  outstandingPayables: number;
  lowStockCount: number;
  salesTrend: TrendPointDto[];
  expenseTrend: TrendPointDto[];
  monthlySales: number;
  monthlyExpenses: number;
  monthlyProfit: number;
}

export interface TrendPointDto {
  date: string;
  amount: number;
}

export interface StockHoldDto {
  id: string;
  productId: string;
  productName: string;
  unit: string;
  contactName: string;
  quantity: number;
  notes?: string;
  status: string;
  source?: string;
  createdAtUtc: string;
}

export interface ActivityFeedDto {
  id: string;
  refId: string;
  type: string;
  description: string;
  amount?: number;
  contactName?: string;
  recordedBy?: string;
  source?: string;
  paymentStatus?: string;
  paymentMethod?: string;
  details?: string;
  createdAtUtc: string;
}

export interface PaginatedActivityResult {
  items: ActivityFeedDto[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface RecentActivityDto {
  type: string;
  description: string;
  amount?: number;
  createdAtUtc: string;
}

export interface DashboardInsightsDto {
  topProducts: TopProductInsightDto[];
  expenseCategories: CategoryBreakdownDto[];
  paymentStatus: PaymentStatusBreakdownDto[];
  receivablesAging: AgingBucketDto[];
  dailyNet: DailyNetDto[];
  topCustomers: TopCustomerDto[];
}

export interface TopProductInsightDto {
  productName: string;
  revenue: number;
  quantity: number;
  unit: string;
}

export interface CategoryBreakdownDto {
  category: string;
  amount: number;
}

export interface PaymentStatusBreakdownDto {
  status: string;
  amount: number;
  count: number;
}

export interface AgingBucketDto {
  bucket: string;
  amount: number;
  contacts: AgingBucketContactDto[];
}

export interface AgingBucketContactDto {
  contactName: string;
  amount: number;
  daysOld: number;
}

export interface DailyNetDto {
  date: string;
  sales: number;
  expenses: number;
  net: number;
}

export interface TopCustomerDto {
  contactName: string;
  revenue: number;
  saleCount: number;
}

export interface DailySummaryDto {
  date: string;
  totalSales: number;
  saleCount: number;
  totalExpenses: number;
  netCashIn: number;
  outstandingReceivables: number;
  outstandingPayables: number;
  lowStockCount: number;
  lowStockItems: ProductDto[];
}

export interface WeeklySummaryDto {
  weekStart: string;
  weekEnd: string;
  totalSales: number;
  totalExpenses: number;
  estimatedProfit: number;
  isProfitEstimate: boolean;
  topProducts: TopProductDto[];
  lowStockItems: ProductDto[];
  topDebtors: OutstandingBalanceDto[];
}

export interface TopProductDto {
  productId: string;
  productName: string;
  unit: string;
  totalQuantitySold: number;
  totalRevenue: number;
}

export interface DeadStockItemDto {
  productId: string;
  productName: string;
  unit: string;
  currentStock: number;
  daysSinceLastSale: number;
}

export interface StockoutPredictionDto {
  productId: string;
  productName: string;
  unit: string;
  currentStock: number;
  dailyRate: number;
  daysLeft: number;
  restockQty: number;
  urgency: string;
}

export interface ProductProfitDto {
  productName: string;
  revenue: number;
  cost: number;
  profit: number;
  margin: number;
}

export interface StaffSalesDto {
  staffName: string;
  totalRevenue: number;
  saleCount: number;
  items: StaffSaleItemDto[];
}

export interface StaffSaleItemDto {
  productName: string;
  unit: string;
  quantity: number;
  revenue: number;
}

export interface CashPositionDto {
  totalSalesThisMonth: number;
  totalExpensesThisMonth: number;
  estimatedCashIn: number;
  outstandingReceivables: number;
  outstandingPayables: number;
  netPosition: number;
  isEstimate: boolean;
}

// ─── Advanced reports (Shop+ tier) ────────────────────────────────────────

export interface AgingContactDto {
  contactId: string;
  contactName: string;
  bucket0To30: number;
  bucket31To60: number;
  bucket61To90: number;
  bucket90Plus: number;
  total: number;
  oldestDays: number;
}

export interface AgingReportDto {
  total0To30: number;
  total31To60: number;
  total61To90: number;
  total90Plus: number;
  grandTotal: number;
  contacts: AgingContactDto[];
}

export interface MonthlyPnlDto {
  month: string;
  previousMonth: string;
  revenue: number;
  previousRevenue: number;
  costOfGoodsSold: number;
  previousCostOfGoodsSold: number;
  grossProfit: number;
  previousGrossProfit: number;
  operatingExpenses: number;
  previousOperatingExpenses: number;
  netProfit: number;
  previousNetProfit: number;
  grossMarginPercent: number;
  netMarginPercent: number;
  isEstimate: boolean;
}

export interface ExpenseCategoryDto {
  category: string;
  amount: number;
  percentOfTotal: number;
  entryCount: number;
}

export interface ExpenseBreakdownDto {
  month: string;
  totalExpenses: number;
  categories: ExpenseCategoryDto[];
}

export interface InventoryTurnoverDto {
  productId: string;
  productName: string;
  unit: string;
  currentStock: number;
  soldLast30Days: number;
  dailyVelocity: number;
  daysOfStockRemaining: number;
  costOfGoodsSold: number;
  inventoryValue: number;
  turnoverRatio: number;
  classification: "Fast" | "Healthy" | "Slow" | "Dead";
}

export interface TopCustomerDetailDto {
  contactId: string;
  contactName: string;
  totalRevenue: number;
  transactionCount: number;
  percentOfTotal: number;
  lastPurchaseAtUtc?: string;
}

export interface TopCustomersReportDto {
  totalRevenue: number;
  topCustomerPercent: number;
  concentrationRisk: boolean;
  customers: TopCustomerDetailDto[];
}

export interface SalesHeatmapCellDto {
  dayOfWeek: number;
  hour: number;
  revenue: number;
  saleCount: number;
}

export interface SalesHeatmapDto {
  weeksAnalyzed: number;
  peakRevenue: number;
  peakDayOfWeek: number;
  peakHour: number;
  cells: SalesHeatmapCellDto[];
}

export interface MonthlyTrendPointDto {
  month: string;
  revenue: number;
  expenses: number;
  profit: number;
  transactionCount: number;
}

export interface MonthlyTrendDto {
  points: MonthlyTrendPointDto[];
}

export interface PaymentMethodMonthDto {
  month: string;
  cash: number;
  transfer: number;
  pos: number;
  credit: number;
  other: number;
}

export interface PaymentMethodSplitDto {
  months: PaymentMethodMonthDto[];
  totalCash: number;
  totalTransfer: number;
  totalPos: number;
  totalCredit: number;
  totalOther: number;
}

export interface CustomerReliabilityDto {
  contactId: string;
  contactName: string;
  paidReceivables: number;
  averageDaysToPay: number;
  totalPaid: number;
  classification: "Prompt" | "Regular" | "Slow" | "Late" | "Unknown";
}

export interface WastageItemDto {
  productId: string;
  productName: string;
  unit: string;
  quantityDamaged: number;
  estimatedLoss: number;
  eventCount: number;
}

export interface WastageReportDto {
  period: string;
  totalValue: number;
  eventCount: number;
  topProducts: WastageItemDto[];
}

export interface AvgTransactionPointDto {
  month: string;
  averageValue: number;
  transactionCount: number;
}

export interface AvgTransactionValueDto {
  points: AvgTransactionPointDto[];
}

export interface RetentionMonthDto {
  month: string;
  newCustomers: number;
  returningCustomers: number;
  newRevenue: number;
  returningRevenue: number;
}

export interface CustomerRetentionDto {
  months: RetentionMonthDto[];
}

export interface ReorderSuggestionDto {
  productId: string;
  productName: string;
  unit: string;
  currentStock: number;
  dailyVelocity: number;
  suggestedReorderQty: number;
  estimatedCost: number;
  urgency: "Critical" | "High" | "Normal";
}

export interface ProductAffinityDto {
  productA: string;
  productB: string;
  coOccurrenceCount: number;
  combinedRevenue: number;
}
