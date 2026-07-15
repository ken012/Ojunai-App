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
  /**
   * Optional because the localStorage-cached user intentionally omits PII
   * (phone, DOB) — those fields only populate after /auth/me returns.
   * Server responses always include them.
   */
  phoneNumber?: string;
  email?: string;
  emailVerified?: boolean;
  role: string;
  dateOfBirth?: string;
  /** Phase 6 — "whatsapp" | "telegram". Drives where outbound alerts/summaries are delivered. */
  alertChannel?: string;
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
  alertDashboardLowStock?: boolean;
  alertDashboardDailySummary?: boolean;
  alertDashboardLargeSale?: boolean;
  alertDashboardAgedReceivable?: boolean;
  alertDashboardStaffChanges?: boolean;
  dailySalesGoal?: number | null;
  /** Public path to the business's custom dashboard background, e.g. /uploads/businesses/.../bg.jpg. Null = no custom background. */
  backgroundImageUrl?: string | null;
  /** 0..1; opacity of the white/dark overlay sitting between the image and content. Higher = more legible, less image visible. */
  backgroundImageOpacity?: number;
  confirmLargeSales?: boolean;
  confirmLargeSaleThreshold?: number;
  confirmLargeSalesTelegram?: boolean;
  confirmLargeSaleThresholdTelegram?: number;
  confirmLargeSalesMessenger?: boolean;
  confirmLargeSaleThresholdMessenger?: number;
  variantsEnabled?: boolean;
  accountNumber?: string;
  voiceAIEnabled?: boolean;
  voiceAIPlanStatus?: string;
  plan?: string;
  subscribedPlan?: string;
  trialEndsAt?: string;
  isActive: boolean;
  // Receipts
  address?: string;
  vatEnabled?: boolean;
  vatRate?: number;
  taxId?: string;
  receiptHeaderText?: string;
  receiptFooterText?: string;
  receiptAccentColor?: string;
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
  aliases?: string[];
  voiceDescription?: string;
  barcode?: string;
  supplierId?: string;
  leadTimeDays?: number;
  isBundle?: boolean;
  tracksBatches?: boolean;
  createdAtUtc: string;
}

export interface ProductBatchDto {
  id: string;
  productId: string;
  productName: string;
  unit: string;
  quantity: number;
  expiryDate?: string;
  lotNumber?: string;
  daysToExpiry?: number | null;
  isExpired: boolean;
  receivedAtUtc: string;
}

export interface BundleComponentDto {
  componentProductId: string;
  componentName: string;
  unit: string;
  componentStock: number;
  quantity: number;
}

export interface BundleDto {
  productId: string;
  isBundle: boolean;
  components: BundleComponentDto[];
}

// ── Variants (styles) ────────────────────────────────────────
export interface VariantAxisDto {
  name: string;
  values: string[];
}

export interface VariantDto {
  productId: string;
  name: string;
  options: Record<string, string>;
  sku?: string;
  barcode?: string;
  unit: string;
  sellingPrice?: number;
  costPrice?: number;
  currentStock: number;
  lowStockThreshold: number;
  isLowStock: boolean;
}

export interface VariantGroupDto {
  id: string;
  name: string;
  category?: string;
  axes: VariantAxisDto[];
  variantCount: number;
  totalStock: number;
  lowStockCount: number;
  minPrice?: number;
  maxPrice?: number;
  createdAtUtc: string;
  variants: VariantDto[];
}

// ── Purchasing ───────────────────────────────────────────────
export type PurchaseOrderStatus = "Draft" | "Sent" | "PartiallyReceived" | "Received" | "Cancelled";

export interface PurchaseOrderItemDto {
  id: string;
  productId?: string;
  productName: string;
  unit: string;
  quantityOrdered: number;
  quantityReceived: number;
  unitCost: number;
  lineTotal: number;
}

export interface PurchaseOrderDto {
  id: string;
  poNumber: string;
  supplierId?: string;
  supplierName?: string;
  status: PurchaseOrderStatus;
  currency: string;
  totalAmount: number;
  notes?: string;
  expectedAtUtc?: string;
  recordedByName?: string;
  createdAtUtc: string;
  sentAtUtc?: string;
  receivedAtUtc?: string;
  cancelledAtUtc?: string;
  items: PurchaseOrderItemDto[];
}

// ── Stocktake (physical counts) ──────────────────────────────
export type StocktakeStatus = "Draft" | "Completed" | "Cancelled";

export interface StocktakeItemDto {
  id: string;
  productId: string;
  productName: string;
  unit: string;
  systemQuantity: number;
  countedQuantity?: number | null;
  unitCost: number;
  variance?: number | null;
  varianceValue?: number | null;
}

export interface StocktakeDto {
  id: string;
  reference: string;
  status: StocktakeStatus;
  scope?: string;
  notes?: string;
  recordedByName?: string;
  totalItems: number;
  countedItems: number;
  varianceItems: number;
  netVarianceValue: number;
  createdAtUtc: string;
  completedAtUtc?: string;
  cancelledAtUtc?: string;
  items: StocktakeItemDto[];
}

export interface SaleSummaryDto {
  id: string;
  totalAmount: number;
  paymentStatus: string;
  paymentMethod?: string;
  itemCount: number;
  itemSummary?: string;
  contactId?: string | null;
  customerName?: string;
  recordedByName?: string;
  source?: string;
  createdAtUtc: string;
  deletedAtUtc?: string;
}

export interface SaleDto extends SaleSummaryDto {
  paymentMethod?: string;
  notes?: string;
  vatAmount?: number;
  receiptNumber?: string | null;
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
  paymentMethod?: string;
  source?: string;
  createdAtUtc: string;
}

export interface ContactDto {
  id: string;
  name: string;
  phoneNumber?: string;
  email?: string;
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
  // Only present on "action" (audit-log) rows — drive per-action icons + deep-links.
  action?: string;      // dotted code, e.g. "product.deleted"
  entityType?: string;  // "Product", "Contact", "Staff", "Business", "Billing", "Channel", "User", …
  entityId?: string;
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

export interface WeeklySalesPointDto {
  weekStart: string;
  weekEnd: string;
  label: string;
  revenue: number;
  saleCount: number;
  avgOrderValue: number;
  growthPercent: number | null;
  movingAvg: number;
}

export interface SalesComparisonDto {
  currentRevenue: number;
  currentSaleCount: number;
  currentAvgOrder: number;
  previousRevenue: number;
  previousSaleCount: number;
  previousAvgOrder: number;
  revenueChangePercent: number;
  saleCountChangePercent: number;
  avgOrderChangePercent: number;
  currentLabel: string;
  previousLabel: string;
}

export interface CategoryRevenueItemDto {
  category: string;
  revenue: number;
  saleCount: number;
  percentOfTotal: number;
}

export interface CategoryRevenueDto {
  categories: CategoryRevenueItemDto[];
  totalRevenue: number;
  uncategorizedRevenue: number;
}

export interface OutstandingContactDto {
  contactId: string;
  contactName: string;
  amount: number;
  daysOld: number;
}

export interface OutstandingDebtSummaryDto {
  totalReceivables: number;
  totalPayables: number;
  netPosition: number;
  overdueContactCount: number;
  topReceivables: OutstandingContactDto[];
  topPayables: OutstandingContactDto[];
}

export interface CashFlowWeekDto {
  label: string;
  cashIn: number;
  cashOut: number;
  net: number;
  runningBalance: number;
}

export interface CashFlowForecastDto {
  actuals: CashFlowWeekDto[];
  forecast: CashFlowWeekDto[];
  projectedMonthEndBalance: number;
  avgWeeklyCashIn: number;
  avgWeeklyCashOut: number;
}

export interface WeeklySalesTrendDto {
  weeks: WeeklySalesPointDto[];
  avgWeeklyRevenue: number;
  bestWeekRevenue: number;
  bestWeekLabel: string;
  worstWeekRevenue: number;
  worstWeekLabel: string;
  avgGrowthPercent: number;
  totalWeeks: number;
  totalRevenue: number;
  totalSales: number;
}
