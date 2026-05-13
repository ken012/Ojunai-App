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
    public DbSet<MessageLog> MessageLogs => Set<MessageLog>();
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

    // ── Multi-channel messaging (Phase 1 refactor) — additive ─────────────────
    public DbSet<ContactIdentity> ContactIdentities => Set<ContactIdentity>();

    // ── Channel linking (Phase 2: Telegram, future: Messenger) ────────────────
    public DbSet<ChannelLinkToken> ChannelLinkTokens => Set<ChannelLinkToken>();

    // ── Telegram pending actions (Phase 2.8: callback flows) ──────────────────
    public DbSet<PendingTelegramAction> PendingTelegramActions => Set<PendingTelegramAction>();

    // ── Admin observability (Phase 7) ──
    public DbSet<AdminAuditEntry> AdminAuditEntries => Set<AdminAuditEntry>();
    public DbSet<AdminMetricSnapshot> AdminMetricSnapshots => Set<AdminMetricSnapshot>();

    // ── Email deliverability ──
    // Suppression list populated by SES bounce/complaint SNS notifications. EmailService
    // checks this on every send so we never re-hit a known-bad address.
    public DbSet<SuppressedEmail> SuppressedEmails => Set<SuppressedEmail>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        base.OnModelCreating(mb);

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
            e.Property(x => x.Version).IsRowVersion();
            e.HasIndex(x => x.ImportBatchId).HasFilter("\"ImportBatchId\" IS NOT NULL");
            e.ToTable(t => t.HasCheckConstraint("CK_Product_CurrentStock_NonNegative", "\"CurrentStock\" >= 0"));
            e.HasOne(x => x.Business)
             .WithMany(x => x.Products)
             .HasForeignKey(x => x.BusinessId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<Sale>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.BusinessId, x.CreatedAtUtc });
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

            // "Find active add-ons for this business" is the hot path for gating.
            e.HasIndex(x => new { x.BusinessId, x.Status });
            // Per-business uniqueness for non-stackable add-ons is enforced in code (the
            // catalog defines stackable=false), not at the DB level — Quantity is part of
            // the row and active rows can legitimately repeat for stackable codes.
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
}
