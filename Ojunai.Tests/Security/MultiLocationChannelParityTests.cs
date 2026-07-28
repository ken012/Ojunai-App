using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.Models;
using Ojunai.API.Services;
using Ojunai.API.Services.Channels;
using Ojunai.API.Services.Interfaces;
using Xunit;

namespace Ojunai.Tests.Security;

/// <summary>
/// Telegram/Messenger bot parity for multi-location (branch switching + attribution). The channel
/// handlers are too heavy to construct directly, so these tests lock the two channel-agnostic pieces
/// they now depend on: <see cref="LocationScope.Push"/> (branch attribution that restores cleanly so a
/// scope can't leak into the next pooled job) and <see cref="LocationChatService"/> (the picker + switch
/// the handlers surface as quick-reply buttons). The plan gate and access scoping must match WhatsApp.
///
/// Note: <see cref="AppDbContext"/> auto-creates the "Main" default location for a newly-added business
/// (the dual-write mirror), so tests reference it via <c>biz.DefaultLocationId</c> and only add extras.
/// </summary>
public class MultiLocationChannelParityTests
{
    private sealed class NoopActivityLogger : IActivityLogger
    {
        public Task LogAsync(Guid businessId, string action, string entityType, Guid? entityId,
            string? entityName, string summary, string? details = null, ActivityActor? actor = null)
            => Task.CompletedTask;
    }

    private static AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("mlparity-" + Guid.NewGuid())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    private static LocationChatService NewService(AppDbContext db) =>
        new(db, new LocationAccessService(db), new PlanGuard(db, new NoopActivityLogger()));

    // Returns the business with its auto-created "Main" default location already provisioned.
    private static async Task<Business> AddBizAsync(AppDbContext db, string plan = "scale")
    {
        var biz = new Business { Name = "T", AccountNumber = "A" + Guid.NewGuid().ToString("N")[..9], Plan = plan };
        db.Businesses.Add(biz);
        await db.SaveChangesAsync();
        return biz;
    }

    // Adds an EXTRA branch (the default "Main" already exists). isDefault stays false.
    private static async Task<Location> AddLocAsync(AppDbContext db, Guid bizId, string name, bool isActive = true)
    {
        var l = new Location { BusinessId = bizId, Name = name, IsDefault = false, IsActive = isActive };
        db.Locations.Add(l);
        await db.SaveChangesAsync();
        return l;
    }

    private static async Task<User> AddUserAsync(AppDbContext db, Guid bizId, UserRole role, Guid? selected = null)
    {
        var u = new User { BusinessId = bizId, FullName = "U", PhoneNumber = "p" + Guid.NewGuid().ToString("N")[..12], Role = role, SelectedLocationId = selected };
        db.Users.Add(u);
        await db.SaveChangesAsync();
        return u;
    }

    // ── LocationScope.Push ──────────────────────────────────────────────────────

    [Fact]
    public void Push_RestoresPreviousValue_OnDispose()
    {
        Assert.Null(LocationScope.Current);
        var branch = Guid.NewGuid();
        using (LocationScope.Push(branch))
            Assert.Equal(branch, LocationScope.Current);
        Assert.Null(LocationScope.Current); // restored — no leak to the next job
    }

    [Fact]
    public void Push_Nested_RestoresOuterValue()
    {
        var outer = Guid.NewGuid();
        var inner = Guid.NewGuid();
        using (LocationScope.Push(outer))
        {
            Assert.Equal(outer, LocationScope.Current);
            using (LocationScope.Push(inner))
                Assert.Equal(inner, LocationScope.Current);
            Assert.Equal(outer, LocationScope.Current); // inner dispose restores outer, not null
        }
        Assert.Null(LocationScope.Current);
    }

    // ── Picker: gating ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Picker_SingleLocation_ReturnsTerminalMessage_NoButtons()
    {
        await using var db = NewContext();
        var biz = await AddBizAsync(db); // only the auto "Main"
        var owner = await AddUserAsync(db, biz.Id, UserRole.Owner);

        var picker = await NewService(db).BuildPickerAsync(biz.Id, owner.Id, owner.Role);

        Assert.Empty(picker.Options);
        Assert.Contains("Main", picker.Text);
    }

    [Fact]
    public async Task Picker_Unentitled_MultiLocation_IsGated()
    {
        await using var db = NewContext();
        var biz = await AddBizAsync(db, plan: "starter"); // no multi-location entitlement
        await AddLocAsync(db, biz.Id, "Ikeja");
        var owner = await AddUserAsync(db, biz.Id, UserRole.Owner);

        var picker = await NewService(db).BuildPickerAsync(biz.Id, owner.Id, owner.Role);

        Assert.Empty(picker.Options); // plan gate → no switching, even though 2 locations exist
        Assert.Contains("Scale", picker.Text);
    }

    [Fact]
    public async Task Picker_Owner_MultiLocation_ListsAll_FlagsCurrent()
    {
        await using var db = NewContext();
        var biz = await AddBizAsync(db);
        var mainId = biz.DefaultLocationId!.Value;
        var ikeja = await AddLocAsync(db, biz.Id, "Ikeja");
        var owner = await AddUserAsync(db, biz.Id, UserRole.Owner, selected: ikeja.Id);

        var picker = await NewService(db).BuildPickerAsync(biz.Id, owner.Id, owner.Role);

        Assert.Equal(2, picker.Options.Count);
        Assert.Contains(picker.Options, o => o.Id == mainId && !o.IsCurrent);
        Assert.Contains(picker.Options, o => o.Id == ikeja.Id && o.IsCurrent);
    }

    [Fact]
    public async Task Picker_RestrictedStaff_PinnedToOneBranch_SeesTerminalMessage()
    {
        await using var db = NewContext();
        var biz = await AddBizAsync(db);
        var ikeja = await AddLocAsync(db, biz.Id, "Ikeja");
        var sales = await AddUserAsync(db, biz.Id, UserRole.Sales);
        db.UserLocations.Add(new UserLocation { UserId = sales.Id, LocationId = ikeja.Id });
        await db.SaveChangesAsync();

        var picker = await NewService(db).BuildPickerAsync(biz.Id, sales.Id, sales.Role);

        Assert.Empty(picker.Options); // only one accessible branch → nothing to switch
        Assert.Contains("Ikeja", picker.Text);
    }

    // ── Switch: apply + validation ──────────────────────────────────────────────

    [Fact]
    public async Task Apply_Owner_ValidBranch_PersistsSelection()
    {
        await using var db = NewContext();
        var biz = await AddBizAsync(db);
        var ikeja = await AddLocAsync(db, biz.Id, "Ikeja");
        var owner = await AddUserAsync(db, biz.Id, UserRole.Owner);

        var reply = await NewService(db).ApplySelectionAsync(biz.Id, owner.Id, owner.Role, ikeja.Id);

        Assert.Contains("Ikeja", reply);
        Assert.Equal(ikeja.Id, (await db.Users.FindAsync(owner.Id))!.SelectedLocationId);
    }

    [Fact]
    public async Task Apply_InactiveBranch_Rejected_SelectionUnchanged()
    {
        await using var db = NewContext();
        var biz = await AddBizAsync(db);
        var closed = await AddLocAsync(db, biz.Id, "Closed", isActive: false);
        var owner = await AddUserAsync(db, biz.Id, UserRole.Owner);

        var reply = await NewService(db).ApplySelectionAsync(biz.Id, owner.Id, owner.Role, closed.Id);

        Assert.Contains("isn't available", reply);
        Assert.Null((await db.Users.FindAsync(owner.Id))!.SelectedLocationId);
    }

    [Fact]
    public async Task Apply_RestrictedStaff_InaccessibleBranch_Rejected()
    {
        await using var db = NewContext();
        var biz = await AddBizAsync(db);
        var mainId = biz.DefaultLocationId!.Value;
        var ikeja = await AddLocAsync(db, biz.Id, "Ikeja");
        var sales = await AddUserAsync(db, biz.Id, UserRole.Sales);
        db.UserLocations.Add(new UserLocation { UserId = sales.Id, LocationId = ikeja.Id });
        await db.SaveChangesAsync();

        // Staffer assigned only to Ikeja tries to switch to the Main branch → refused; selection stays null.
        var reply = await NewService(db).ApplySelectionAsync(biz.Id, sales.Id, sales.Role, mainId);

        Assert.Contains("don't have access", reply);
        Assert.Null((await db.Users.FindAsync(sales.Id))!.SelectedLocationId);
    }

    // ── Branch-command detection ────────────────────────────────────────────────

    [Theory]
    [InlineData("branches", true)]
    [InlineData("locations", true)]
    [InlineData("switch branch", true)]
    [InlineData("my locations", true)]
    [InlineData("Branches?", true)]
    [InlineData("sold 2 rice for 1500", false)]
    [InlineData("what's my branch doing", false)]
    [InlineData("", false)]
    public void IsBranchCommand_MatchesOnlyTheCommand(string text, bool expected)
        => Assert.Equal(expected, LocationChatService.IsBranchCommand(text));
}
