using BizPilot.API.Models;
using Microsoft.EntityFrameworkCore;

namespace BizPilot.API.Data;

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
    }
}
