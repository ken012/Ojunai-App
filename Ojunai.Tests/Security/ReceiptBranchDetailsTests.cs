using Ojunai.API.Models;
using Ojunai.API.Services;
using Xunit;

namespace Ojunai.Tests.Security;

/// <summary>
/// Per-branch receipt details (ReceiptService.ResolveReceiptOrg): a sale recorded at a branch prints that
/// branch's address + phone; blank branch fields fall back to the business; phone has no business fallback;
/// and the branch NAME line only shows for a non-default branch (so single-location / Main receipts are
/// byte-for-byte unchanged).
/// </summary>
public class ReceiptBranchDetailsTests
{
    private static Business Biz() => new()
    {
        Name = "Ojunai", Address = "1 Head Office Rd", City = "Lagos", State = "Lagos", Country = "Nigeria",
    };

    [Fact]
    public void NoLocation_UsesBusinessDetails_NoBranchName_NoPhone()
    {
        var org = ReceiptService.ResolveReceiptOrg(Biz(), null);
        Assert.Null(org.BranchName);
        Assert.Equal("1 Head Office Rd", org.Address);
        Assert.Equal("Lagos, Lagos, Nigeria", org.CityStateCountry);
        Assert.Null(org.Phone); // there is no business phone to fall back to
    }

    [Fact]
    public void Branch_WithOwnDetails_UsesBranch_AndShowsBranchName()
    {
        var loc = new Location { Name = "Ikeja", IsDefault = false, Address = "5 Ikeja Rd", City = "Ikeja", State = "Lagos", Phone = "+234 800 111 2222" };
        var org = ReceiptService.ResolveReceiptOrg(Biz(), loc);
        Assert.Equal("Ikeja", org.BranchName);
        Assert.Equal("5 Ikeja Rd", org.Address);
        Assert.Equal("Ikeja, Lagos, Nigeria", org.CityStateCountry);
        Assert.Equal("+234 800 111 2222", org.Phone);
    }

    [Fact]
    public void Branch_BlankFields_FallBackToBusiness_PhoneStaysNull()
    {
        var loc = new Location { Name = "Ikeja", IsDefault = false, Address = "  ", City = null, State = "", Phone = null };
        var org = ReceiptService.ResolveReceiptOrg(Biz(), loc);
        Assert.Equal("1 Head Office Rd", org.Address);      // blank branch address → business
        Assert.Equal("Lagos, Lagos, Nigeria", org.CityStateCountry);
        Assert.Null(org.Phone);                              // branch has none, no business fallback
        Assert.Equal("Ikeja", org.BranchName);               // still labels which branch
    }

    [Fact]
    public void DefaultBranch_HidesBranchNameLine()
    {
        var loc = new Location { Name = "Main", IsDefault = true, Phone = "+234 999" };
        var org = ReceiptService.ResolveReceiptOrg(Biz(), loc);
        Assert.Null(org.BranchName);       // default/Main → no extra name line
        Assert.Equal("+234 999", org.Phone);
        Assert.Equal("1 Head Office Rd", org.Address); // Main left address blank → business
    }
}
