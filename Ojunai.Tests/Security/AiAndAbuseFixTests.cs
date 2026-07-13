using System.Text.Json;
using Ojunai.API.Common;
using Xunit;

namespace Ojunai.Tests.Security;

/// <summary>
/// Regression tests for the AI-action-safety and abuse-case fixes (OJ-12/OJ-13/OJ-14).
/// Pure logic only — no DB / channel required.
/// </summary>
public class AiAndAbuseFixTests
{
    private static JsonElement Payload(string json) => JsonDocument.Parse(json).RootElement;

    // ── OJ-12: destructive/bulk intents must require confirmation ────────────────────────────────

    [Theory]
    [InlineData("delete_product", "{\"deleteAll\":\"true\"}")]
    [InlineData("delete_product", "{\"deleteCategory\":\"Beauty\"}")]
    [InlineData("remove_inventory", "{\"zeroAll\":\"true\"}")]
    [InlineData("record_receivable_payment", "{\"clearAllDebts\":\"true\"}")]
    [InlineData("record_payable_payment", "{\"clearAllDebts\":\"true\"}")]
    public void Destructive_BulkIntents_RequireConfirmation(string intent, string json)
    {
        Assert.True(DestructiveIntentGuard.RequiresConfirmation(intent, Payload(json)));
        Assert.NotNull(DestructiveIntentGuard.DescribeIfDestructive(intent, Payload(json)));
    }

    // ── OJ-13: add_staff must require confirmation and echo who/role ─────────────────────────────

    [Fact]
    public void AddStaff_RequiresConfirmation_AndEchoesDetails()
    {
        var desc = DestructiveIntentGuard.DescribeIfDestructive(
            "add_staff", Payload("{\"fullName\":\"Mary\",\"phoneNumber\":\"+2348012345678\",\"role\":\"Admin\"}"));
        Assert.NotNull(desc);
        Assert.Contains("Mary", desc);
        Assert.Contains("+2348012345678", desc);
    }

    // ── F00/F02: destructive sub-actions smuggled inside batch_action must STILL require confirmation ─

    [Theory]
    [InlineData("{\"complete\":[{\"intent\":\"create_sale\",\"items\":[]},{\"intent\":\"record_receivable_payment\",\"clearAllDebts\":\"true\"}]}")]
    [InlineData("{\"complete\":[{\"intent\":\"create_expense\",\"amount\":\"500\"},{\"intent\":\"remove_inventory\",\"zeroAll\":\"true\"}]}")]
    [InlineData("{\"complete\":[{\"intent\":\"delete_product\",\"deleteAll\":\"true\"}]}")]
    [InlineData("{\"complete\":[{\"intent\":\"create_sale\",\"items\":[]},{\"intent\":\"add_staff\",\"fullName\":\"Mary\",\"phoneNumber\":\"+2348012345678\"}]}")]
    public void BatchAction_WithDestructiveSubAction_RequiresConfirmation(string json)
    {
        Assert.True(DestructiveIntentGuard.RequiresConfirmation("batch_action", Payload(json)));
        Assert.NotNull(DestructiveIntentGuard.DescribeIfDestructive("batch_action", Payload(json)));
    }

    [Theory]
    [InlineData("{\"complete\":[{\"intent\":\"create_sale\",\"items\":[]},{\"intent\":\"create_expense\",\"amount\":\"500\"}]}")]
    [InlineData("{\"complete\":[]}")]
    [InlineData("{}")]
    public void BatchAction_WithoutDestructiveSubAction_DoesNotRequireConfirmation(string json)
    {
        Assert.False(DestructiveIntentGuard.RequiresConfirmation("batch_action", Payload(json)));
    }

    // ── Non-destructive intents must NOT be gated (everyday flows unchanged) ─────────────────────

    [Theory]
    [InlineData("create_sale", "{\"items\":[]}")]
    [InlineData("get_today_sales", "{}")]
    [InlineData("delete_product", "{\"productName\":\"Rice\"}")]                 // single delete — not gated
    [InlineData("record_receivable_payment", "{\"contactName\":\"Ada\",\"amount\":\"5000\"}")] // single payment — not gated
    public void NonDestructiveIntents_DoNotRequireConfirmation(string intent, string json)
    {
        Assert.False(DestructiveIntentGuard.RequiresConfirmation(intent, Payload(json)));
    }

    // ── OJ-14: per-sender rate limit (denial-of-wallet) ─────────────────────────────────────────

    [Fact]
    public void RateLimiter_BlocksAfterCap()
    {
        var sender = "test-sender-" + Guid.NewGuid().ToString("N"); // unique so tests don't interfere
        var blockedAt = -1;
        for (var i = 0; i < 30; i++)
        {
            if (ChannelRateLimiter.IsLimited(sender)) { blockedAt = i; break; }
        }
        // WhatsApp parity is 15/min; the exact cap is internal, but it MUST block within 30 rapid sends.
        Assert.InRange(blockedAt, 1, 20);
    }

    [Fact]
    public void RateLimiter_EmptySender_NotLimited()
    {
        Assert.False(ChannelRateLimiter.IsLimited(""));
        Assert.False(ChannelRateLimiter.IsLimited(null));
    }

    [Fact]
    public void RateLimiter_CapLength_Truncates()
    {
        var capped = ChannelRateLimiter.CapLength(new string('x', 10_000));
        Assert.True(capped.Length <= ChannelRateLimiter.MaxInboundLength);
    }

    [Fact]
    public void RateLimiter_CapLength_ShortText_Unchanged()
    {
        Assert.Equal("sold 2 rice 5000", ChannelRateLimiter.CapLength("sold 2 rice 5000"));
    }
}
