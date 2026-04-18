namespace BizPilot.API.Models;

public class Business
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? BusinessType { get; set; }
    public string Currency { get; set; } = "NGN";
    public string Country { get; set; } = "Nigeria";
    public string Timezone { get; set; } = "Africa/Lagos";
    public string? State { get; set; }
    public string? City { get; set; }
    public Guid OwnerUserId { get; set; }
    public string Plan { get; set; } = "starter";
    public string? SubscribedPlan { get; set; }
    public DateTime? TrialEndsAt { get; set; }
    public bool IsBillable { get; set; } = true;
    public string? PaystackCustomerCode { get; set; }
    public string? PaystackSubscriptionCode { get; set; }
    public string? PaystackPlanCode { get; set; }
    public string? FlutterwaveSubscriptionId { get; set; }
    public string? FlutterwaveCustomerId { get; set; }
    public string BillingProvider { get; set; } = "paystack";
    public string BillingCycle { get; set; } = "monthly";
    public string BillingCurrency { get; set; } = "NGN";
    public bool IsAutoRenew { get; set; } = true;
    public string? PaymentMethod { get; set; }
    public DateTime? SubscriptionEndsAt { get; set; }
    public string? PendingPlanChange { get; set; }
    public decimal LargeSaleThreshold { get; set; } = 100000;
    public string? CustomCategories { get; set; } // JSON array: ["Category1", "Category2"]
    public bool AlertLowStock { get; set; } = true;
    public bool AlertDailySummary { get; set; } = true;
    public bool AlertLargeSale { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Product> Products { get; set; } = new List<Product>();
    public ICollection<Sale> Sales { get; set; } = new List<Sale>();
    public ICollection<Expense> Expenses { get; set; } = new List<Expense>();
    public ICollection<Contact> Contacts { get; set; } = new List<Contact>();
    public ICollection<DailySummary> DailySummaries { get; set; } = new List<DailySummary>();
}
