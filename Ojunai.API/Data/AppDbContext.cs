using Ojunai.API.Common;
using Ojunai.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Business> Businesses => Set<Business>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleItem> SaleItems => Set<SaleItem>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();
    public DbSet<Stocktake> Stocktakes => Set<Stocktake>();
    public DbSet<StocktakeItem> StocktakeItems => Set<StocktakeItem>();
    public DbSet<BundleComponent> BundleComponents => Set<BundleComponent>();
    public DbSet<VariantGroup> VariantGroups => Set<VariantGroup>();
    public DbSet<ProductBatch> ProductBatches => Set<ProductBatch>();
    public DbSet<MessageLog> MessageLogs => Set<MessageLog>();
    public DbSet<InboundMessageClaim> InboundMessageClaims => Set<InboundMessageClaim>();
    public DbSet<DailySummary> DailySummaries => Set<DailySummary>();
    public DbSet<StockHold> StockHolds => Set<StockHold>();
    public DbSet<OnboardingState> OnboardingStates => Set<OnboardingState>();
    public DbSet<PaystackEventLog> PaystackEventLogs => Set<PaystackEventLog>();
    public DbSet<ImportJob> ImportJobs => Set<ImportJob>();
    public DbSet<PendingAction> PendingActions => Set<PendingAction>();
    public DbSet<BillingEvent> BillingEvents => Set<BillingEvent>();
    public DbSet<MobileEvent> MobileEvents => Set<MobileEvent>();
    public DbSet<PhoneVerificationCode> PhoneVerificationCodes => Set<PhoneVerificationCode>();
    public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();
    public DbSet<AccountRecoveryToken> AccountRecoveryTokens => Set<AccountRecoveryToken>();
    public DbSet<Alert> Alerts => Set<Alert>();

    // ── Pricing v2 (Phase 0) — additive, not yet wired into reads/writes ──────
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<BusinessAddOn> BusinessAddOns => Set<BusinessAddOn>();
    public DbSet<ActionUsage> ActionUsages => Set<ActionUsage>();
    public DbSet<BusinessOverride> BusinessOverrides => Set<BusinessOverride>();

    // ── Multi-location (Phase 0) — additive, not yet wired into reads/writes ──
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<ProductLocationStock> ProductLocationStocks => Set<ProductLocationStock>();
    public DbSet<UserLocation> UserLocations => Set<UserLocation>();

    // ── Multi-channel messaging (Phase 1 refactor) — additive ─────────────────
    public DbSet<ContactIdentity> ContactIdentities => Set<ContactIdentity>();

    // ── Channel linking (Phase 2: Telegram, future: Messenger) ────────────────
    public DbSet<ChannelLinkToken> ChannelLinkTokens => Set<ChannelLinkToken>();

    // ── Channel-native signup (Phase 3) — issued when a visitor opts into
    // signup-via-Telegram on /register. Single-use; on consume, the bot creates a
    // User + Business + ContactIdentity and stamps those IDs back onto the token row.
    public DbSet<SignupChannelToken> SignupChannelTokens => Set<SignupChannelToken>();

    // ── Telegram pending actions (Phase 2.8: callback flows) ──────────────────
    public DbSet<PendingTelegramAction> PendingTelegramActions => Set<PendingTelegramAction>();

    // ── Admin observability (Phase 7) ──
    public DbSet<AdminAuditEntry> AdminAuditEntries => Set<AdminAuditEntry>();
    public DbSet<AdminMetricSnapshot> AdminMetricSnapshots => Set<AdminMetricSnapshot>();

    // Append-only user/bot action audit log (create/update/delete across modules).
    public DbSet<ActivityLogEntry> ActivityLogEntries => Set<ActivityLogEntry>();

    // ── Email deliverability ──
    // Suppression list populated by SES bounce/complaint SNS notifications. EmailService
    // checks this on every send so we never re-hit a known-bad address.
    public DbSet<SuppressedEmail> SuppressedEmails => Set<SuppressedEmail>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        base.OnModelCreating(mb);

        mb.Entity<ActivityLogEntry>(e =>
        {
            e.HasIndex(x => new { x.BusinessId, x.CreatedAtUtc });
            e.Property(x => x.ActorName).HasMaxLength(200);
            e.Property(x => x.ActorChannel).HasMaxLength(20);
            e.Property(x => x.Action).HasMaxLength(60);
            e.Property(x => x.EntityType).HasMaxLength(40);
            e.Property(x => x.EntityName).HasMaxLength(300);
            e.Property(x => x.Summary).HasMaxLength(500);
        });

        mb.Entity<Business>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Currency).HasMaxLength(10).HasDefaultValue("NGN");
            e.Property(x => x.Country).HasMaxLength(100).HasDefaultValue("Nigeria");
            e.Property(x => x.Timezone).HasMaxLength(50).HasDefaultValue("Africa/Lagos");
            e.Property(x => x.AccountNumber).HasMaxLength(10).IsRequired();
            e.HasIndex(x => x.AccountNumber).IsUnique();
            e.Property(x => x.VoiceAIPlanStatus).HasMaxLength(20).HasDefaultValue("inactive");
            e.Property(x => x.VoiceAISubscriptionId).HasMaxLength(200);
            e.Property(x => x.BackgroundImageFileName).HasMaxLength(100);
        });

        mb.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.PhoneNumber).IsUnique();
            e.HasIndex(x => x.Email);
            e.HasIndex(x => x.BusinessId);
            e.Property(x => x.PhoneNumber).HasMaxLength(20).IsRequired();
            e.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Email).HasMaxLength(200);
            e.HasOne(x => x.Business)
             .WithMany(x => x.Users)
             .HasForeignKey(x => x.BusinessId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<Product>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.BusinessId, x.Name });
            e.HasIndex(x => x.BusinessId);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.SKU).HasMaxLength(100);
            e.Property(x => x.Unit).HasMaxLength(50).HasDefaultValue("unit");
            e.Property(x => x.CostPrice).HasPrecision(18, 2);
            e.Property(x => x.SellingPrice).HasPrecision(18, 2);
            e.Property(x => x.CurrentStock).HasPrecision(18, 4);
            e.Property(x => x.LowStockThreshold).HasPrecision(18, 4);
            e.Property(x => x.Barcode).HasMaxLength(64);
            e.Property(x => x.Version).IsRowVersion();
            e.HasIndex(x => x.ImportBatchId).HasFilter("\"ImportBatchId\" IS NOT NULL");
            e.HasIndex(x => new { x.BusinessId, x.Barcode }).HasFilter("\"Barcode\" IS NOT NULL");
            e.HasIndex(x => x.VariantGroupId).HasFilter("\"VariantGroupId\" IS NOT NULL");
            e.ToTable(t => t.HasCheckConstraint("CK_Product_CurrentStock_NonNegative", "\"CurrentStock\" >= 0"));
            e.HasOne(x => x.Business)
             .WithMany(x => x.Products)
             .HasForeignKey(x => x.BusinessId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<PurchaseOrder>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.BusinessId, x.CreatedAtUtc });
            e.HasIndex(x => new { x.BusinessId, x.Status });
            e.Property(x => x.PoNumber).HasMaxLength(40).IsRequired();
            e.Property(x => x.SupplierName).HasMaxLength(200);
            e.Property(x => x.Currency).HasMaxLength(10).HasDefaultValue("NGN");
            e.Property(x => x.TotalAmount).HasPrecision(18, 2);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.HasOne(x => x.Business)
             .WithMany()
             .HasForeignKey(x => x.BusinessId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Items)
             .WithOne(x => x.PurchaseOrder)
             .HasForeignKey(x => x.PurchaseOrderId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<PurchaseOrderItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.PurchaseOrderId);
            e.Property(x => x.ProductName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Unit).HasMaxLength(50).HasDefaultValue("unit");
            e.Property(x => x.QuantityOrdered).HasPrecision(18, 4);
            e.Property(x => x.QuantityReceived).HasPrecision(18, 4);
            e.Property(x => x.UnitCost).HasPrecision(18, 2);
            e.Property(x => x.LineTotal).HasPrecision(18, 2);
        });

        mb.Entity<Stocktake>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.BusinessId, x.CreatedAtUtc });
            e.HasIndex(x => new { x.BusinessId, x.Status });
            e.Property(x => x.Reference).HasMaxLength(40).IsRequired();
            e.Property(x => x.Scope).HasMaxLength(200);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.HasOne(x => x.Business)
             .WithMany()
             .HasForeignKey(x => x.BusinessId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Items)
             .WithOne(x => x.Stocktake)
             .HasForeignKey(x => x.StocktakeId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<StocktakeItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.StocktakeId);
            e.Property(x => x.ProductName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Unit).HasMaxLength(50).HasDefaultValue("unit");
            e.Property(x => x.SystemQuantity).HasPrecision(18, 4);
            e.Property(x => x.CountedQuantity).HasPrecision(18, 4);
            e.Property(x => x.UnitCost).HasPrecision(18, 2);
        });

        mb.Entity<BundleComponent>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.BusinessId, x.BundleProductId });
            e.Property(x => x.Quantity).HasPrecision(18, 4);
        });

        mb.Entity<VariantGroup>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.BusinessId, x.CreatedAtUtc });
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Axes).HasMaxLength(1000);
            e.Property(x => x.Category).HasMaxLength(100);
            e.HasOne(x => x.Business)
             .WithMany()
             .HasForeignKey(x => x.BusinessId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<ProductBatch>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.BusinessId, x.ProductId });
            e.HasIndex(x => new { x.BusinessId, x.ExpiryDate });
            e.Property(x => x.Quantity).HasPrecision(18, 4);
            e.Property(x => x.CostPrice).HasPrecision(18, 2);
            e.Property(x => x.LotNumber).HasMaxLength(80);
            e.HasOne(x => x.Product)
             .WithMany()
             .HasForeignKey(x => x.ProductId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<Sale>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.BusinessId, x.CreatedAtUtc });
            e.HasIndex(x => new { x.BusinessId, x.LocationId, x.CreatedAtUtc }); // multi-location (Phase 0, additive)
            e.Property(x => x.TotalAmount).HasPrecision(18, 2);
            e.Property(x => x.PaymentMethod).HasMaxLength(50);
            e.Property(x => x.DeleteReason).HasMaxLength(20);
            e.HasQueryFilter(x => !x.IsDeleted);
            e.HasOne(x => x.Business)
             .WithMany(x => x.Sales)
             .HasForeignKey(x => x.BusinessId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Contact)
             .WithMany(x => x.Sales)
             .HasForeignKey(x => x.ContactId)
             .IsRequired(false)
             .OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Items)
             .WithOne(x => x.Sale)
             .HasForeignKey(x => x.SaleId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<SaleItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.TotalPrice).HasPrecision(18, 2);
            e.Property(x => x.Quantity).HasPrecision(18, 4);
            // Match Sale's IsDeleted filter so reports exclude items from voided sales
            e.HasQueryFilter(x => !x.Sale.IsDeleted);
            e.HasOne(x => x.Product)
             .WithMany(x => x.SaleItems)
             .HasForeignKey(x => x.ProductId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<Expense>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.BusinessId, x.CreatedAtUtc });
            e.HasIndex(x => new { x.BusinessId, x.LocationId, x.CreatedAtUtc }); // multi-location (Phase 0, additive)
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Category).HasMaxLength(100).HasDefaultValue("General");
            e.Property(x => x.ExpenseType).HasMaxLength(20).HasDefaultValue("operating");
            e.Property(x => x.PaidTo).HasMaxLength(200);
            e.HasIndex(x => x.ImportBatchId).HasFilter("\"ImportBatchId\" IS NOT NULL");
            e.HasQueryFilter(x => !x.IsDeleted);
            e.HasOne(x => x.Business)
             .WithMany(x => x.Expenses)
             .HasForeignKey(x => x.BusinessId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<Contact>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.BusinessId, x.Name });
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.PhoneNumber).HasMaxLength(20);
            e.Property(x => x.Email).HasMaxLength(200);
            e.HasIndex(x => x.ImportBatchId).HasFilter("\"ImportBatchId\" IS NOT NULL");
            e.HasOne(x => x.Business)
             .WithMany(x => x.Contacts)
             .HasForeignKey(x => x.BusinessId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<LedgerEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.BusinessId, x.ContactId });
            e.HasIndex(x => new { x.BusinessId, x.EntryType });
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.HasIndex(x => x.ImportBatchId).HasFilter("\"ImportBatchId\" IS NOT NULL");
            e.HasOne(x => x.Contact)
             .WithMany(x => x.LedgerEntries)
             .HasForeignKey(x => x.ContactId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<InventoryTransaction>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.BusinessId, x.ProductId });
            e.HasIndex(x => new { x.BusinessId, x.CreatedAtUtc });
            e.HasIndex(x => new { x.BusinessId, x.LocationId, x.CreatedAtUtc }); // multi-location (Phase 0, additive)
            e.Property(x => x.Quantity).HasPrecision(18, 4);
            e.Property(x => x.UnitCost).HasPrecision(18, 2);
            e.HasOne(x => x.Product)
             .WithMany(x => x.InventoryTransactions)
             .HasForeignKey(x => x.ProductId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<MessageLog>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.BusinessId);
            e.HasIndex(x => x.CreatedAtUtc);
            e.HasIndex(x => x.WhatsAppMessageId);
            e.Property(x => x.Channel).HasMaxLength(50).HasDefaultValue("WhatsApp");
            e.Property(x => x.WhatsAppMessageId).HasMaxLength(100);
            e.Property(x => x.ConfidenceScore).HasPrecision(5, 4);
        });

        mb.Entity<InboundMessageClaim>(e =>
        {
            // Composite PK = the atomic dedup key. A duplicate inbound (provider re-delivery or
            // Hangfire retry) hits a 23505 unique-violation on insert, which the dedup service
            // catches and treats as "already handled".
            e.HasKey(x => new { x.Channel, x.ProviderMessageId });
            e.Property(x => x.ProviderMessageId).HasMaxLength(200);
            e.HasIndex(x => x.ClaimedAtUtc); // supports the retention sweep
        });

        mb.Entity<StockHold>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.BusinessId, x.Status });
            e.HasIndex(x => new { x.BusinessId, x.ProductId });
            e.Property(x => x.ContactName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Quantity).HasPrecision(18, 4);
            e.Property(x => x.Version).IsRowVersion();
            e.HasOne(x => x.Business).WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<DailySummary>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.BusinessId, x.Date }).IsUnique();
            e.Property(x => x.TotalSales).HasPrecision(18, 2);
            e.Property(x => x.TotalExpenses).HasPrecision(18, 2);
            e.Property(x => x.NetCashIn).HasPrecision(18, 2);
            e.Property(x => x.OutstandingReceivables).HasPrecision(18, 2);
            e.Property(x => x.OutstandingPayables).HasPrecision(18, 2);
            e.HasOne(x => x.Business)
             .WithMany(x => x.DailySummaries)
             .HasForeignKey(x => x.BusinessId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<OnboardingState>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.PhoneNumber).IsUnique();
            e.Property(x => x.PhoneNumber).HasMaxLength(20).IsRequired();
            e.Property(x => x.BusinessName).HasMaxLength(200);
            e.Property(x => x.BusinessType).HasMaxLength(100);
            e.Property(x => x.City).HasMaxLength(100);
            e.Property(x => x.OwnerName).HasMaxLength(200);
        });

        mb.Entity<PaystackEventLog>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.EventId).IsUnique();
            e.Property(x => x.EventId).HasMaxLength(200).IsRequired();
            e.Property(x => x.EventType).HasMaxLength(100).IsRequired();
        });

        mb.Entity<ImportJob>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.BusinessId, x.CreatedAtUtc });
            e.HasIndex(x => x.Status);
            e.Property(x => x.FileName).HasMaxLength(500);
            e.HasOne(x => x.Business)
             .WithMany()
             .HasForeignKey(x => x.BusinessId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<PendingAction>(e =>
        {
            e.HasKey(x => x.Id);
            // One pending action per user max — composite unique index enforces "overwrite, don't accumulate".
            e.HasIndex(x => new { x.BusinessId, x.UserId }).IsUnique();
            e.Property(x => x.Intent).HasMaxLength(100).IsRequired();
            e.Property(x => x.AwaitingField).HasMaxLength(100).IsRequired();
            e.Property(x => x.QuestionText).HasMaxLength(2000);
        });

        mb.Entity<BillingEvent>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.BusinessId, x.CreatedAtUtc });
            e.Property(x => x.EventType).HasMaxLength(100).IsRequired();
            e.Property(x => x.Provider).HasMaxLength(50).IsRequired();
            e.Property(x => x.Plan).HasMaxLength(50);
            e.Property(x => x.BillingCycle).HasMaxLength(20);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Currency).HasMaxLength(10);
            e.Property(x => x.TransactionRef).HasMaxLength(200);
            e.Property(x => x.SubscriptionId).HasMaxLength(200);
            e.Property(x => x.PaymentMethod).HasMaxLength(50);
            e.Property(x => x.Status).HasMaxLength(50);
            e.Property(x => x.ErrorDetails).HasMaxLength(2000);
        });

        mb.Entity<MobileEvent>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Name, x.CreatedAtUtc });
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Payload).HasMaxLength(4000);
            e.Property(x => x.IpAddress).HasMaxLength(50);
            e.Property(x => x.UserAgent).HasMaxLength(500);
        });

        mb.Entity<PhoneVerificationCode>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.PhoneNumber, x.Purpose, x.ExpiresAtUtc });
            e.Property(x => x.PhoneNumber).HasMaxLength(30).IsRequired();
            e.Property(x => x.HashedCode).HasMaxLength(200).IsRequired();
            e.Property(x => x.Purpose).HasConversion<int>().HasDefaultValue(PhoneVerificationPurpose.SignupVerification);
        });

        mb.Entity<EmailVerificationToken>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.ExpiresAtUtc });
            e.Property(x => x.HashedToken).HasMaxLength(200).IsRequired();
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<AccountRecoveryToken>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.ExpiresAtUtc });
            e.Property(x => x.HashedToken).HasMaxLength(200).IsRequired();
            e.Property(x => x.RequestIp).HasMaxLength(50);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<Alert>(e =>
        {
            e.HasKey(x => x.Id);
            // Bell queries are "business + unread" or "user + unread" — index supports both.
            e.HasIndex(x => new { x.BusinessId, x.CreatedAtUtc });
            e.HasIndex(x => new { x.BusinessId, x.UserId, x.ReadAtUtc });
            e.HasIndex(x => new { x.BusinessId, x.DedupeKey });
            e.Property(x => x.Type).HasConversion<int>();
            e.Property(x => x.Severity).HasConversion<int>();
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Body).HasMaxLength(2000).IsRequired();
            e.Property(x => x.LinkUrl).HasMaxLength(500);
            e.Property(x => x.MetadataJson).HasMaxLength(4000);
            e.Property(x => x.DedupeKey).HasMaxLength(200);
        });

        // ── Pricing v2 entity configurations ───────────────────────────────────
        // All four are additive in Phase 0 — no reads/writes yet. Indices match the
        // expected v1 access patterns in Phase 1+ so we don't pay for re-indexing later.

        mb.Entity<Subscription>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ProductLine).HasConversion<int>();
            e.Property(x => x.Tier).HasMaxLength(40).IsRequired();
            e.Property(x => x.Status).HasMaxLength(20).IsRequired();
            e.Property(x => x.BillingCycle).HasMaxLength(10);
            e.Property(x => x.BillingCurrency).HasMaxLength(10);
            e.Property(x => x.Provider).HasMaxLength(40);
            e.Property(x => x.ProviderSubscriptionId).HasMaxLength(200);

            // Look up "active subscription for business + product line" frequently.
            e.HasIndex(x => new { x.BusinessId, x.ProductLine, x.Status });
            // Webhook / reconciliation joins on provider IDs.
            e.HasIndex(x => x.ProviderSubscriptionId);
        });

        mb.Entity<BusinessAddOn>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.AddOnCode).HasMaxLength(60).IsRequired();
            e.Property(x => x.Status).HasMaxLength(20).IsRequired();
            e.Property(x => x.BilledCurrency).HasMaxLength(10);
            e.Property(x => x.ProviderSubscriptionId).HasMaxLength(200);

            // "Find active add-ons for this business" is the hot path for gating.
            e.HasIndex(x => new { x.BusinessId, x.Status });
            // Recurring webhooks look up by ProviderSubscriptionId to identify which
            // pack/business a renewal charge applies to. Sparse — non-recurring rows are null.
            e.HasIndex(x => x.ProviderSubscriptionId).HasFilter("\"ProviderSubscriptionId\" IS NOT NULL");
            // Per-business uniqueness for non-stackable add-ons is enforced in code (the
            // catalog defines stackable=false), not at the DB level — Quantity is part of
            // the row and active rows can legitimately repeat for stackable codes.
            //
            // EXCEPTION: WhatsApp packs are non-stackable and money-bearing, so enforce
            // "at most one active whatsapp_pack per business" at the DB level. A concurrent
            // double-activation would otherwise leave two active packs (merchant gets double
            // the paid allowance). Partial unique index — matches the cancel-then-insert
            // invariant the activation code follows. Activation cancels the old pack BEFORE
            // inserting the new one (see UpsertWhatsAppPackAddOnAsync / HandleWhatsAppPackVerifiedAsync).
            e.HasIndex(x => x.BusinessId)
                .HasDatabaseName("IX_BusinessAddOns_OneActiveWhatsAppPack")
                .HasFilter("\"Status\" = 'active' AND \"AddOnCode\" LIKE 'whatsapp_pack.%'")
                .IsUnique();
        });

        mb.Entity<ActionUsage>(e =>
        {
            // Composite primary key: a business has one row per (product_line, period_start).
            // INSERT ... ON CONFLICT (BusinessId, ProductLine, PeriodStartUtc) DO UPDATE
            // bumps Count atomically.
            e.HasKey(x => new { x.BusinessId, x.ProductLine, x.PeriodStartUtc });
            e.Property(x => x.ProductLine).HasConversion<int>();
        });

        mb.Entity<BusinessOverride>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.OverrideType).HasMaxLength(40).IsRequired();
            e.Property(x => x.LegacyTier).HasMaxLength(40);
            e.Property(x => x.LegacyPriceCurrency).HasMaxLength(10);
            e.Property(x => x.ReasonNote).HasMaxLength(500);

            // Lookup "active overrides for this business right now" is the hot path.
            e.HasIndex(x => new { x.BusinessId, x.OverrideType });
            e.HasIndex(x => x.ExpiresAtUtc);
        });

        // ── Multi-location entity configurations (Phase 0) ─────────────────────
        // Additive — nothing reads/writes these yet. The LocationId columns added to
        // Sale/Expense/InventoryTransaction/etc. are plain nullable scalars (no FK) so historical rows
        // and existing writers are unaffected; only the three tables below carry relationships.
        mb.Entity<Location>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.BusinessId);
            // Exactly one default location per business.
            e.HasIndex(x => x.BusinessId)
                .HasDatabaseName("IX_Locations_OneDefaultPerBusiness")
                .HasFilter("\"IsDefault\" = true")
                .IsUnique();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Type).HasMaxLength(20).HasDefaultValue("branch");
            e.Property(x => x.Address).HasMaxLength(300);
            e.Property(x => x.City).HasMaxLength(100);
            e.Property(x => x.State).HasMaxLength(100);
            e.Property(x => x.Currency).HasMaxLength(10);
            e.Property(x => x.Timezone).HasMaxLength(50);
            e.Property(x => x.ReceiptPrefix).HasMaxLength(20);
            e.HasOne(x => x.Business)
             .WithMany()
             .HasForeignKey(x => x.BusinessId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<ProductLocationStock>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProductId, x.LocationId }).IsUnique();
            e.HasIndex(x => new { x.BusinessId, x.LocationId });
            e.Property(x => x.CurrentStock).HasPrecision(18, 4);
            e.Property(x => x.LowStockThreshold).HasPrecision(18, 4);
            // No rowversion in Phase 1 — see ProductLocationStock.cs; the mirror is last-write-wins so it
            // can never fail a primary save with a concurrency exception.
            e.ToTable(t => t.HasCheckConstraint("CK_ProductLocationStock_CurrentStock_NonNegative", "\"CurrentStock\" >= 0"));
            e.HasOne(x => x.Product)
             .WithMany()
             .HasForeignKey(x => x.ProductId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Location)
             .WithMany()
             .HasForeignKey(x => x.LocationId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<UserLocation>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.LocationId }).IsUnique();
            e.HasIndex(x => x.LocationId);
            e.HasOne(x => x.User)
             .WithMany()
             .HasForeignKey(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Location)
             .WithMany()
             .HasForeignKey(x => x.LocationId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<ContactIdentity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Channel).HasConversion<int>();
            e.Property(x => x.ChannelIdentityValue).HasMaxLength(120).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(200);

            // Globally unique per (channel, handle) — no two users can share the same phone/chat_id/PSID.
            e.HasIndex(x => new { x.Channel, x.ChannelIdentityValue }).IsUnique();
            // Lookup "all identities for this user" hits this; not unique because a user can have
            // multiple identities of the same channel (work + personal WhatsApp, multi-Page Messenger, etc.).
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.BusinessId);
        });

        mb.Entity<ChannelLinkToken>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Channel).HasConversion<int>();
            e.Property(x => x.Token).HasMaxLength(80).IsRequired();
            e.Property(x => x.BoundToIdentity).HasMaxLength(120);

            // Lookup "find unconsumed token by value" — hot path for /start flow.
            e.HasIndex(x => x.Token).IsUnique();
            // For cleanup jobs that purge old tokens.
            e.HasIndex(x => x.ExpiresAtUtc);
        });

        mb.Entity<SignupChannelToken>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Channel).HasConversion<int>();
            e.Property(x => x.Token).HasMaxLength(80).IsRequired();
            e.Property(x => x.ConsumedByIdentity).HasMaxLength(120);
            e.Property(x => x.RequestIp).HasMaxLength(64);

            e.HasIndex(x => x.Token).IsUnique();
            e.HasIndex(x => x.ExpiresAtUtc);
        });

        mb.Entity<PendingTelegramAction>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ChatId).HasMaxLength(64).IsRequired();
            e.Property(x => x.Token).HasMaxLength(32).IsRequired();
            e.Property(x => x.ActionType).HasMaxLength(40).IsRequired();
            e.Property(x => x.PayloadJson).HasColumnType("jsonb");

            // Lookup by token is the hot path on callback resume.
            e.HasIndex(x => x.Token).IsUnique();
            e.HasIndex(x => x.ExpiresAtUtc);
        });

        mb.Entity<AdminAuditEntry>(e =>
        {
            e.Property(x => x.Endpoint).HasMaxLength(200).IsRequired();
            e.Property(x => x.Ip).HasMaxLength(64);
            e.Property(x => x.KeyPrefix).HasMaxLength(24);
            e.Property(x => x.QueryString).HasMaxLength(500);
            e.HasIndex(x => x.CreatedAtUtc);
            e.HasIndex(x => x.KeyPrefix);
        });

        mb.Entity<AdminMetricSnapshot>(e =>
        {
            e.Property(x => x.MetricName).HasMaxLength(40).IsRequired();
            e.Property(x => x.ChannelFilter).HasMaxLength(20);
            e.Property(x => x.ValueText).HasMaxLength(80);
            // Unique per (metric, channel filter, date) so a re-run of the daily job is a no-op
            // rather than a duplicate row. Channel filter is nullable; Postgres treats nulls as
            // distinct so we keep two distinct unique constraints to cover both cases.
            e.HasIndex(x => new { x.MetricName, x.ChannelFilter, x.CapturedDate }).IsUnique();
            e.HasIndex(x => new { x.MetricName, x.CapturedDate });
        });

        mb.Entity<SuppressedEmail>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(320).IsRequired();
            e.Property(x => x.Reason).HasMaxLength(20).IsRequired();
            e.Property(x => x.BounceType).HasMaxLength(40);
            e.Property(x => x.BounceSubType).HasMaxLength(40);
            // RawPayload is jsonb so we can ad-hoc query specific fields if we ever debug
            // a delivery issue ("show me every bounce with bounceSubType=NoEmail").
            e.Property(x => x.RawPayload).HasColumnType("jsonb");
            // Email is normalized lowercase at write time — a plain unique index is enough.
            e.HasIndex(x => x.Email).IsUnique();
        });
    }

    // ── Multi-location dual-write (Phase 1) ──────────────────────────────────────
    // Best-effort mirror of Product.CurrentStock into the default-location ProductLocationStock, plus a
    // default "Main" Location for every new Business. Product.CurrentStock stays the AUTHORITATIVE source
    // for every read (nothing reads ProductLocationStock yet — Phase 2 flips that); this just keeps the
    // per-location rows in sync FROM Phase 1 on, so the Phase 2 read-cutover is race-free. Single-location
    // today (location creation is Phase 2), so a product's one PLS row mirrors its CurrentStock — except a
    // product whose business isn't backfilled yet (DefaultLocationId still null) is skipped, which the
    // backfill/reconciliation later repairs.
    //
    // CRITICAL — the mirror can NEVER break the primary save: it reads/computes first (guarded by
    // try/catch, touching the context via reads only), then applies pure in-memory changes that cannot
    // throw. On ANY failure it skips silently — the worst case is ProductLocationStock drift, which the
    // idempotent backfill/reconciliation (scripts/backfill-multi-location.sql) detects and repairs.

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        // No SynchronizationContext under ASP.NET Core → GetResult() is deadlock-safe. The only sync
        // SaveChanges caller is admin audit logging (no Product/Business changes), so this no-ops there.
        StampLocationScopedEntitiesAsync(CancellationToken.None).GetAwaiter().GetResult();
        MirrorMultiLocationAsync(CancellationToken.None).GetAwaiter().GetResult();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        await StampLocationScopedEntitiesAsync(cancellationToken);
        await MirrorMultiLocationAsync(cancellationToken);
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Multi-location attribution: stamp <see cref="ILocationScoped"/> rows being INSERTED with the ambient
    /// selected location — but ONLY when the business is genuinely multi-location (&gt;1 active location) and
    /// the selected location is one of its active ones. Exactly the <c>SelectedLocationForAsync</c> gate, so
    /// single-location businesses, "All locations", and bot/background saves leave LocationId null =
    /// business-wide, byte-for-byte as before. Best-effort: attribution is a convenience, never a reason to
    /// break a primary save — any failure leaves rows unstamped (null = business-wide, the safe fallback).
    /// Runs before base.SaveChanges so the value rides the INSERT. Only touches rows whose LocationId is still
    /// null, so anything a service stamped explicitly (Sale/Expense) is left as-is.
    /// </summary>
    private async Task StampLocationScopedEntitiesAsync(CancellationToken ct)
    {
        try
        {
            if (LocationScope.Current is not { } ambient) return; // nothing selected → business-wide, unchanged

            var pending = ChangeTracker.Entries<ILocationScoped>()
                .Where(e => e.State == EntityState.Added && e.Entity.LocationId == null && e.Entity.BusinessId != Guid.Empty)
                .Select(e => e.Entity)
                .ToList();
            if (pending.Count == 0) return;

            var bizIds = pending.Select(e => e.BusinessId).Distinct().ToList();
            var activeByBiz = (await Locations
                    .Where(l => bizIds.Contains(l.BusinessId) && l.IsActive)
                    .Select(l => new { l.BusinessId, l.Id })
                    .ToListAsync(ct))
                .GroupBy(x => x.BusinessId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToHashSet());

            foreach (var e in pending)
                if (activeByBiz.TryGetValue(e.BusinessId, out var ids) && ids.Count > 1 && ids.Contains(ambient))
                    e.LocationId = ambient;
        }
        catch
        {
            // Best-effort — see summary. Unstamped rows fall back to business-wide (null), never a broken save.
        }
    }

    private async Task MirrorMultiLocationAsync(CancellationToken ct)
    {
        var newLocations = new List<(Business biz, Location loc)>();
        var stockToAdd = new List<ProductLocationStock>();
        var stockToUpdate = new List<(ProductLocationStock pls, decimal newStock)>();
        try
        {
            // New businesses get a default "Main" location (receipt counter/prefix seeded from the business).
            foreach (var b in ChangeTracker.Entries<Business>()
                         .Where(e => e.State == EntityState.Added && e.Entity.DefaultLocationId == null)
                         .Select(e => e.Entity))
            {
                newLocations.Add((b, new Location
                {
                    BusinessId = b.Id, Name = "Main", Type = "branch", IsDefault = true, IsActive = true,
                    NextReceiptNumber = b.NextReceiptNumber, ReceiptPrefix = b.ReceiptPrefix,
                }));
            }
            var pendingBizDefault = newLocations.ToDictionary(x => x.biz.Id, x => x.loc.Id);

            // Products that were added, or whose CurrentStock changed. Keep the ENTRY (not just the entity)
            // so the multi-location branch can read the pre-change CurrentStock for a delta.
            var productEntries = ChangeTracker.Entries<Product>()
                .Where(e => e.State == EntityState.Added
                    || (e.State == EntityState.Modified && e.Property(p => p.CurrentStock).IsModified))
                .ToList();
            if (productEntries.Count == 0 && newLocations.Count == 0) return;

            // Load the involved businesses' locations in one query — for default resolution, single-vs-multi
            // detection (active-location count), and validating the ambient X-Location-Id against the business.
            var existingBizIds = productEntries.Select(e => e.Entity.BusinessId).Distinct()
                .Where(id => !pendingBizDefault.ContainsKey(id)).ToList();
            var locRows = existingBizIds.Count == 0
                ? new List<(Guid BusinessId, Guid Id, bool IsDefault, bool IsActive)>()
                : (await Locations.Where(l => existingBizIds.Contains(l.BusinessId))
                        .Select(l => new { l.BusinessId, l.Id, l.IsDefault, l.IsActive })
                        .ToListAsync(ct))
                    .Select(x => (x.BusinessId, x.Id, x.IsDefault, x.IsActive)).ToList();

            var defaultByBiz = new Dictionary<Guid, Guid>();
            var activeCountByBiz = new Dictionary<Guid, int>();
            var activeIdsByBiz = new Dictionary<Guid, HashSet<Guid>>();
            foreach (var (bizId, defId) in pendingBizDefault)
            {
                defaultByBiz[bizId] = defId; activeCountByBiz[bizId] = 1;
                activeIdsByBiz[bizId] = new HashSet<Guid> { defId };
            }
            foreach (var g in locRows.GroupBy(l => l.BusinessId))
            {
                var def = g.FirstOrDefault(l => l.IsDefault);
                if (def.Id != Guid.Empty) defaultByBiz[g.Key] = def.Id;
                activeCountByBiz[g.Key] = g.Count(l => l.IsActive);
                activeIdsByBiz[g.Key] = g.Where(l => l.IsActive).Select(l => l.Id).ToHashSet();
            }

            var ambient = LocationScope.Current; // resolved X-Location-Id for this request (if any)

            // Batch-load existing per-location stock rows for the involved products (one query).
            var productIds = productEntries.Select(e => e.Entity.Id).ToList();
            var existing = await ProductLocationStocks.Where(x => productIds.Contains(x.ProductId)).ToListAsync(ct);
            var byKey = existing.ToDictionary(x => (x.ProductId, x.LocationId));
            foreach (var pls in ProductLocationStocks.Local) byKey.TryAdd((pls.ProductId, pls.LocationId), pls);

            foreach (var entry in productEntries)
            {
                var p = entry.Entity;
                if (!defaultByBiz.TryGetValue(p.BusinessId, out var defaultLoc)) continue; // not backfilled → skip

                Guid targetLoc;
                decimal newValue;
                if (activeCountByBiz.GetValueOrDefault(p.BusinessId, 1) <= 1)
                {
                    // SINGLE-LOCATION — unchanged from Phase 1: the default PLS mirrors CurrentStock exactly.
                    targetLoc = defaultLoc;
                    newValue = p.CurrentStock;
                }
                else
                {
                    // MULTI-LOCATION — route the CurrentStock DELTA to the request's resolved location (validated
                    // against the business; else the default), clamped ≥ 0 so it can never trip the non-negative
                    // check constraint. Product.CurrentStock stays the business-wide roll-up the service set, so
                    // SUM(per-location) == Product.CurrentStock holds for RELATIVE ops (sale/stock-in/out). Absolute
                    // set-stock ops + per-location availability are follow-ups — see docs/multi-location-spec.md.
                    targetLoc = ambient is { } a && activeIdsByBiz.GetValueOrDefault(p.BusinessId) is { } ids && ids.Contains(a)
                        ? a : defaultLoc;
                    var original = entry.State == EntityState.Added ? 0m
                        : entry.Property(x => x.CurrentStock).OriginalValue;
                    var delta = p.CurrentStock - original;
                    var current = byKey.TryGetValue((p.Id, targetLoc), out var cr) ? cr.CurrentStock : 0m;
                    newValue = Math.Max(0m, current + delta);
                }

                if (byKey.TryGetValue((p.Id, targetLoc), out var row))
                    stockToUpdate.Add((row, newValue));
                else
                {
                    var added = new ProductLocationStock
                    {
                        BusinessId = p.BusinessId, ProductId = p.Id, LocationId = targetLoc, CurrentStock = newValue,
                    };
                    stockToAdd.Add(added);
                    byKey[(p.Id, targetLoc)] = added; // guard against a duplicate within this same save
                }
            }
        }
        catch
        {
            // Best-effort: never let the mirror break the primary save. Phase B below has not run, so the
            // change tracker holds no mirror mutations — the primary save proceeds untouched.
            return;
        }

        // Apply. Structurally non-throwing (fresh Guid Ids can't collide with tracked entities; the
        // per-(product,location) dedup above prevents duplicate adds; setting a property is total). Still
        // wrapped defensively so that even an impossible failure fully detaches the mirror's own additions
        // and reverts the DefaultLocationId it set — the caller's primary save is never corrupted.
        try
        {
            foreach (var (biz, loc) in newLocations) { Locations.Add(loc); biz.DefaultLocationId = loc.Id; }
            foreach (var pls in stockToAdd) ProductLocationStocks.Add(pls);
            foreach (var (pls, newStock) in stockToUpdate) pls.CurrentStock = newStock;
        }
        catch
        {
            foreach (var en in ChangeTracker.Entries<ProductLocationStock>().Where(en => en.State == EntityState.Added).ToList())
                en.State = EntityState.Detached;
            foreach (var en in ChangeTracker.Entries<Location>().Where(en => en.State == EntityState.Added).ToList())
                en.State = EntityState.Detached;
            foreach (var (biz, _) in newLocations) biz.DefaultLocationId = null;
        }
    }
}
