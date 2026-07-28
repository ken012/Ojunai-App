using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.DTOs.Contacts;
using Ojunai.API.Models;
using Ojunai.API.Services;
using Ojunai.API.Services.Interfaces;
using Xunit;

namespace Ojunai.Tests.Security;

/// <summary>
/// Per-branch scoping of the ledger (receivables/payables) and contact balances — the fix for "Contacts,
/// Ledger and the dashboard Today still show Main when I'm on Branch B". Debts are stamped with the branch
/// they're recorded at (existing ones are backfilled to the default in the migration); reads filter to the
/// selected branch. Contacts stay a business-wide list but carry an origin-branch label. Single-location
/// businesses must be byte-for-byte unchanged (locId resolves to null → predicates vanish).
/// </summary>
public class MultiLocationLedgerScopingTests
{
    private sealed class NoopActivityLogger : IActivityLogger
    {
        public Task LogAsync(Guid businessId, string action, string entityType, Guid? entityId,
            string? entityName, string summary, string? details = null, ActivityActor? actor = null)
            => Task.CompletedTask;
    }

    private static AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("mlledger-" + Guid.NewGuid())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    private static LedgerService Ledger(AppDbContext db) => new(db, new LocationStockService(db));
    private static ContactService Contacts(AppDbContext db) => new(db, new NoopActivityLogger(), new LocationStockService(db));

    private static async Task<Business> AddBizAsync(AppDbContext db, string plan = "scale")
    {
        var biz = new Business { Name = "T", AccountNumber = "A" + Guid.NewGuid().ToString("N")[..9], Plan = plan };
        db.Businesses.Add(biz);
        await db.SaveChangesAsync(); // auto-creates the default "Main" location
        return biz;
    }

    private static async Task<Location> AddLocAsync(AppDbContext db, Guid bizId, string name)
    {
        var l = new Location { BusinessId = bizId, Name = name, IsActive = true };
        db.Locations.Add(l);
        await db.SaveChangesAsync();
        return l;
    }

    private static async Task<Contact> AddContactAsync(AppDbContext db, Guid bizId, string name)
    {
        var c = new Contact { BusinessId = bizId, Name = name, Type = ContactType.Customer };
        db.Contacts.Add(c);
        await db.SaveChangesAsync();
        return c;
    }

    // Adds a ledger entry, stamped (via the central stamp) with `at` — mirrors how a debt recorded while
    // viewing that branch gets attributed.
    private static async Task AddLedgerAsync(AppDbContext db, Guid bizId, Guid contactId, LedgerEntryType type, decimal amount, Guid? at)
    {
        using (LocationScope.Push(at))
        {
            db.LedgerEntries.Add(new LedgerEntry { BusinessId = bizId, ContactId = contactId, EntryType = type, Amount = amount });
            await db.SaveChangesAsync();
        }
    }

    // ── Ledger scoping ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Ledger_OutstandingBalances_ScopeToSelectedBranch()
    {
        await using var db = NewContext();
        var biz = await AddBizAsync(db);
        var mainId = biz.DefaultLocationId!.Value;
        var ikeja = await AddLocAsync(db, biz.Id, "Ikeja");

        var ada = await AddContactAsync(db, biz.Id, "Ada");   // owes at Main
        var bola = await AddContactAsync(db, biz.Id, "Bola");  // owes at Ikeja
        await AddLedgerAsync(db, biz.Id, ada.Id, LedgerEntryType.Receivable, 100, at: mainId);
        await AddLedgerAsync(db, biz.Id, bola.Id, LedgerEntryType.Receivable, 50, at: ikeja.Id);

        // View Ikeja → only Bola's 50.
        using (LocationScope.Push(ikeja.Id))
        {
            var atIkeja = await Ledger(db).GetOutstandingBalancesAsync(biz.Id, "receivable");
            Assert.Single(atIkeja);
            Assert.Equal("Bola", atIkeja[0].ContactName);
            Assert.Equal(50, atIkeja[0].TotalReceivable);
        }

        // View Main → only Ada's 100.
        using (LocationScope.Push(mainId))
        {
            var atMain = await Ledger(db).GetOutstandingBalancesAsync(biz.Id, "receivable");
            Assert.Single(atMain);
            Assert.Equal("Ada", atMain[0].ContactName);
        }

        // "All locations" (no branch selected) → both.
        var all = await Ledger(db).GetOutstandingBalancesAsync(biz.Id, "receivable");
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task Ledger_SingleLocation_Unchanged_ReturnsEverything()
    {
        await using var db = NewContext();
        var biz = await AddBizAsync(db); // only the default location
        var ada = await AddContactAsync(db, biz.Id, "Ada");
        await AddLedgerAsync(db, biz.Id, ada.Id, LedgerEntryType.Receivable, 100, at: biz.DefaultLocationId);

        // Even if some code path pushed a location, a single-location business resolves locId to null → unchanged.
        using (LocationScope.Push(biz.DefaultLocationId))
        {
            var res = await Ledger(db).GetOutstandingBalancesAsync(biz.Id, "receivable");
            Assert.Single(res);
            Assert.Equal(100, res[0].TotalReceivable);
        }
    }

    // ── Contact balances + origin-branch label ───────────────────────────────────

    [Fact]
    public async Task Contact_CreatedUnderBranch_IsStampedAndLabeled()
    {
        await using var db = NewContext();
        var biz = await AddBizAsync(db);
        var ikeja = await AddLocAsync(db, biz.Id, "Ikeja");

        ContactDto created;
        using (LocationScope.Push(ikeja.Id))
            created = await Contacts(db).CreateAsync(biz.Id, new CreateContactRequest { Name = "Chidi", Type = ContactType.Customer });

        Assert.Equal("Ikeja", created.CreatedAtBranch);
        var row = await db.Contacts.FindAsync(created.Id);
        Assert.Equal(ikeja.Id, row!.LocationId); // stamped by the central pass
    }

    [Fact]
    public async Task Contact_SingleLocation_HasNoBranchLabel()
    {
        await using var db = NewContext();
        var biz = await AddBizAsync(db);

        var created = await Contacts(db).CreateAsync(biz.Id, new CreateContactRequest { Name = "Solo", Type = ContactType.Customer });

        Assert.Null(created.CreatedAtBranch); // single-location → nothing to label
    }

    [Fact]
    public async Task Contact_NullLocation_LabelFallsBackToDefaultBranch()
    {
        // Simulates a pre-backfill / recorded-under-All contact: LocationId null → labeled as the default branch.
        await using var db = NewContext();
        var biz = await AddBizAsync(db);
        await AddLocAsync(db, biz.Id, "Ikeja"); // makes it multi-location
        var c = await AddContactAsync(db, biz.Id, "Legacy"); // created with no branch scope → LocationId null

        var dto = await Contacts(db).GetByIdAsync(biz.Id, c.Id);
        Assert.Equal("Main", dto.CreatedAtBranch); // coalesced to the default location's name
    }

    [Fact]
    public async Task Contact_Balances_ScopeToSelectedBranch()
    {
        await using var db = NewContext();
        var biz = await AddBizAsync(db);
        var mainId = biz.DefaultLocationId!.Value;
        var ikeja = await AddLocAsync(db, biz.Id, "Ikeja");
        var ada = await AddContactAsync(db, biz.Id, "Ada");
        await AddLedgerAsync(db, biz.Id, ada.Id, LedgerEntryType.Receivable, 80, at: mainId);
        await AddLedgerAsync(db, biz.Id, ada.Id, LedgerEntryType.Receivable, 30, at: ikeja.Id);

        // At Ikeja, Ada's balance is just the 30 recorded there (not the 110 business-wide).
        using (LocationScope.Push(ikeja.Id))
        {
            var page = await Contacts(db).GetAllAsync(biz.Id, 1, 50, null, null, null);
            var ada2 = page.Items.Single(i => i.Name == "Ada");
            Assert.Equal(30, ada2.OutstandingReceivable);
        }

        // Business-wide (All) → the full 110.
        var allPage = await Contacts(db).GetAllAsync(biz.Id, 1, 50, null, null, null);
        Assert.Equal(110, allPage.Items.Single(i => i.Name == "Ada").OutstandingReceivable);
    }
}
