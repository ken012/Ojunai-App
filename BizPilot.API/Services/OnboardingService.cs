using System.Text;
using System.Text.Json;
using BizPilot.API.Data;
using BizPilot.API.Models;
using BizPilot.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BizPilot.API.Services;

/// <summary>
/// Multi-step conversational signup flow for new users messaging BizPilot via WhatsApp for the first time.
///
/// Flow:
///   1. Unknown number sees a menu: 1=new business, 2=staff, 3=help
///   2. On "1", we walk through: business name → type → city → owner name → confirmation
///   3. On confirmation, we create Business + Owner User in the database and send credentials
///
/// Each step's progress is stored in the OnboardingStates table, keyed by phone number.
/// If the user pauses, they can resume within 24 hours (expiry) or 30 minutes (idle prompt).
/// Business type input is normalized via Claude (so "I sell clothes" → "Fashion").
/// At the confirmation step, Claude also detects corrections like "no, the name is Ada Beauty".
/// </summary>
public class OnboardingService
{
    private readonly AppDbContext _db;
    private readonly IWhatsAppService _whatsApp;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<OnboardingService> _logger;

    // Canonical list of business types we classify user input into. Everything else falls back to "Other".
    private static readonly string[] ValidBusinessTypes = { "Retail", "Beauty", "Food", "Fashion", "Electronics", "Services", "Agriculture", "Health", "Education", "Other" };

    // After this long, we discard the partial signup entirely and treat the user as new again.
    private const int ResumeWindowHours = 24;

    // After this long without replying, we prompt the user with "continue or restart?" on their next message.
    private const int IdleTimeoutMinutes = 30;

    // Included on every bot message so users always know how to bail out.
    private const string Footer = "\n\n_Reply 'restart' to start over, 'cancel' to stop._";

    public OnboardingService(AppDbContext db, IWhatsAppService whatsApp, IHttpClientFactory httpFactory, ILogger<OnboardingService> logger)
    {
        _db = db;
        _whatsApp = whatsApp;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    /// <summary>
    /// Entry point called from WhatsAppService.HandleInboundAsync when no active user is found for the incoming phone.
    /// Returns a step name string (e.g. "onboarding:business_name", "onboarding:complete") used for funnel analytics,
    /// or null if this phone already has an active account (in which case WhatsAppService handles the message normally).
    /// </summary>
    public async Task<string?> HandleIfOnboardingAsync(string from, string phone, string text)
    {
        // Defensive check — if the phone belongs to an active user, fall through so WhatsAppService processes the message.
        // (The caller already filters by IsActive, but we double-check in case something changed between calls.)
        var existing = await _db.Users.AnyAsync(u => u.PhoneNumber == phone && u.IsActive);
        if (existing) return null;

        var state = await _db.OnboardingStates.FirstOrDefaultAsync(s => s.PhoneNumber == phone);
        var trimmed = text.Trim();
        var lower = trimmed.ToLower();

        if (state == null)
        {
            if (lower is "1" or "sign up" or "signup" or "register" or "start")
            {
                state = new OnboardingState { PhoneNumber = phone, Step = OnboardingStep.BusinessName };
                _db.OnboardingStates.Add(state);
                await _db.SaveChangesAsync();
                await Send(from, $"Let's set up your business! What's your *business name*?{Footer}");
                return "onboarding:started";
            }

            if (lower is "2" or "staff" or "invited")
            {
                await Send(from, "Ask your business owner to add you by sending this message on their WhatsApp:\n\n*add staff [your name] [your phone number]*\n\nExample: \"add staff Mary 08012345678\"\n\nOnce added, you can start recording sales and checking stock right here on WhatsApp.");
                return "onboarding:staff_inquiry";
            }

            if (lower is "3" or "help")
            {
                await Send(from, "BizPilot is a WhatsApp-first business management tool. You can record sales, track inventory, manage expenses, and more — all through chat.\n\nVisit bizpilot-ai.com to learn more, or reply *1* to create your business account.");
                return "onboarding:help";
            }

            await Send(from, "👋 Welcome to *BizPilot*!\n\nYour number isn't linked to a business yet.\n\nReply:\n*1* — Create a new business\n*2* — I was invited as staff\n*3* — Help");
            return "onboarding:menu";
        }

        if (lower is "restart" or "start over")
        {
            state.Step = OnboardingStep.BusinessName;
            state.BusinessName = null;
            state.BusinessType = null;
            state.City = null;
            state.OwnerName = null;
            state.LastActivityUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await Send(from, $"Starting over. What's your *business name*?{Footer}");
            return "onboarding:restart";
        }

        if (lower is "cancel" or "stop" or "nevermind" or "never mind" or "quit")
        {
            _db.OnboardingStates.Remove(state);
            await _db.SaveChangesAsync();
            await Send(from, "Signup cancelled. Message us anytime to start again.");
            return "onboarding:cancelled";
        }

        if ((DateTime.UtcNow - state.LastActivityUtc).TotalHours > ResumeWindowHours)
        {
            _db.OnboardingStates.Remove(state);
            await _db.SaveChangesAsync();
            await Send(from, "👋 Welcome to *BizPilot*!\n\nYour number isn't linked to a business yet.\n\nReply:\n*1* — Create a new business\n*2* — I was invited as staff\n*3* — Help");
            return "onboarding:expired";
        }

        if ((DateTime.UtcNow - state.LastActivityUtc).TotalMinutes > IdleTimeoutMinutes && state.Step != OnboardingStep.Menu)
        {
            state.LastActivityUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            if (lower is not ("continue" or "yes" or "go" or "resume"))
            {
                var progress = state.BusinessName != null ? $" (business: {state.BusinessName})" : "";
                await Send(from, $"Welcome back! We were setting up your business{progress}.\n\nReply *continue* to pick up where you left off, or *restart* to start over.");
                return "onboarding:resume_prompt";
            }
        }

        state.LastActivityUtc = DateTime.UtcNow;

        switch (state.Step)
        {
            case OnboardingStep.BusinessName:
                state.BusinessName = CleanName(trimmed);
                state.Step = OnboardingStep.BusinessType;
                await _db.SaveChangesAsync();
                await Send(from, $"Got it — *{state.BusinessName}*.\n\nWhat type of business is this?\n(e.g. Retail, Beauty, Food, Fashion, Electronics, Services){Footer}");
                return "onboarding:business_name";

            case OnboardingStep.BusinessType:
                state.BusinessType = await ClassifyBusinessType(trimmed);
                state.Step = OnboardingStep.City;
                await _db.SaveChangesAsync();
                await Send(from, $"Business type: *{state.BusinessType}*.\n\nWhat city are you in?{Footer}");
                return "onboarding:business_type";

            case OnboardingStep.City:
                state.City = CleanName(trimmed);
                state.Step = OnboardingStep.OwnerName;
                await _db.SaveChangesAsync();
                await Send(from, $"City: *{state.City}*.\n\nWhat's your full name? (This will be your account name){Footer}");
                return "onboarding:city";

            case OnboardingStep.OwnerName:
                state.OwnerName = CleanName(trimmed);
                state.Step = OnboardingStep.Confirmation;
                await _db.SaveChangesAsync();
                await Send(from, $"Here's what I have:\n\n" +
                    $"🏪 *Business:* {state.BusinessName}\n" +
                    $"📋 *Type:* {state.BusinessType}\n" +
                    $"📍 *City:* {state.City}\n" +
                    $"👤 *Owner:* {state.OwnerName}\n\n" +
                    $"Reply *confirm* to create your account, or tell me what to fix.");
                return "onboarding:owner_name";

            case OnboardingStep.Confirmation:
                if (lower is "confirm" or "yes" or "ok" or "correct" or "looks good" or "go ahead" or "create")
                {
                    var created = await CreateAccountAsync(from, phone, state);
                    return created ? "onboarding:complete" : "onboarding:create_failed";
                }

                var corrected = await DetectAndApplyCorrection(trimmed, state);
                if (corrected)
                {
                    await _db.SaveChangesAsync();
                    await Send(from, $"Updated! Here's the revised info:\n\n" +
                        $"🏪 *Business:* {state.BusinessName}\n" +
                        $"📋 *Type:* {state.BusinessType}\n" +
                        $"📍 *City:* {state.City}\n" +
                        $"👤 *Owner:* {state.OwnerName}\n\n" +
                        $"Reply *confirm* to create your account, or tell me what to fix.");
                    return "onboarding:correction";
                }

                await Send(from, "Reply *confirm* to create your account, or tell me what to fix.\n\nExamples: \"business name is Ada Beauty\" or \"city is Lagos\"");
                return "onboarding:awaiting_confirm";

            default:
                _db.OnboardingStates.Remove(state);
                await _db.SaveChangesAsync();
                return null;
        }
    }

    private async Task<bool> CreateAccountAsync(string from, string phone, OnboardingState state)
    {
        var phoneExists = await _db.Users.AnyAsync(u => u.PhoneNumber == phone && u.IsActive);
        if (phoneExists)
        {
            _db.OnboardingStates.Remove(state);
            await _db.SaveChangesAsync();
            await Send(from, "This phone number is already registered. Try logging into the dashboard at app.bizpilot-ai.com");
            return false;
        }

        // If a deactivated user holds this phone (from a previous business), free it by swapping
        // to a placeholder instead of deleting. This preserves audit history — their RecordedByUserId
        // references in the old business's sales/expenses stay intact.
        var deactivated = await _db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == phone && !u.IsActive);
        if (deactivated != null)
        {
            deactivated.PhoneNumber = $"x{deactivated.Id.ToString("N")[..18]}";
            await _db.SaveChangesAsync();
        }

        var business = new Business
        {
            Name = state.BusinessName!,
            BusinessType = state.BusinessType,
            City = state.City,
            Plan = "starter",
            TrialEndsAt = DateTime.UtcNow.AddDays(30)
        };
        _db.Businesses.Add(business);

        var tempPassword = GeneratePassword();
        var user = new User
        {
            BusinessId = business.Id,
            FullName = state.OwnerName!,
            PhoneNumber = phone,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(tempPassword),
            Role = UserRole.Owner,
            MustChangePassword = true
        };
        _db.Users.Add(user);
        business.OwnerUserId = user.Id;

        _db.OnboardingStates.Remove(state);
        await _db.SaveChangesAsync();

        // Log signup event but redact the phone number for privacy (only last 4 digits retained).
        var phoneRedacted = phone.Length >= 4 ? new string('*', phone.Length - 4) + phone[^4..] : "****";
        _logger.LogInformation("WhatsApp signup: {Business} by {Owner} ({Phone})", business.Name, user.FullName, phoneRedacted);

        await Send(from, $"✅ *Account created!*\n\n" +
            $"🏪 {business.Name} is ready to go.\n" +
            $"📱 You're on the *Starter* plan with a 30-day free trial.\n\n" +
            $"*Dashboard login:*\n" +
            $"🌐 app.bizpilot-ai.com\n" +
            $"📞 Phone: {phone}\n" +
            $"🔑 Password: {tempPassword}\n" +
            $"⚠️ You'll be asked to change this on first login.\n\n" +
            $"*Try these now:*\n" +
            $"• \"Sold 5 bags of rice at 3000\"\n" +
            $"• \"Bought 10 bottles of shampoo\"\n" +
            $"• \"How much did I make today?\"\n" +
            $"• \"plans\" to see upgrade options\n\n" +
            $"Say *plans* anytime to compare plans. Happy selling! 🎉");

        return true;
    }

    private async Task<string> ClassifyBusinessType(string raw)
    {
        try
        {
            var options = string.Join(", ", ValidBusinessTypes);
            var prompt = $"Classify this business description into exactly one of: {options}. Return ONLY the category name.\n\nInput: \"{raw}\"";
            var result = await AskClaude(prompt);
            var cleaned = result?.Trim().Trim('"') ?? "Other";
            return ValidBusinessTypes.FirstOrDefault(t => t.Equals(cleaned, StringComparison.OrdinalIgnoreCase)) ?? "Other";
        }
        catch { return "Other"; }
    }

    private async Task<bool> DetectAndApplyCorrection(string text, OnboardingState state)
    {
        try
        {
            var prompt = $"User is correcting signup info. Current:\n" +
                $"- businessName: {state.BusinessName}\n" +
                $"- businessType: {state.BusinessType}\n" +
                $"- city: {state.City}\n" +
                $"- ownerName: {state.OwnerName}\n\n" +
                $"User says: \"{text}\"\n\n" +
                $"Which field are they correcting and what's the new value?\n" +
                $"Return JSON only: {{\"field\":\"businessName|businessType|city|ownerName\",\"value\":\"new value\"}}\n" +
                $"If not a correction, return {{\"field\":null}}";

            var result = await AskClaude(prompt);
            if (string.IsNullOrWhiteSpace(result)) return false;

            var clean = result.Trim();
            if (clean.StartsWith("```")) clean = clean.Split('\n').Skip(1).TakeWhile(l => !l.StartsWith("```")).Aggregate((a, b) => a + b);

            using var json = JsonDocument.Parse(clean);
            var field = json.RootElement.GetProperty("field").GetString();
            if (string.IsNullOrEmpty(field) || field == "null") return false;

            var value = json.RootElement.GetProperty("value").GetString() ?? "";
            switch (field.ToLower())
            {
                case "businessname": state.BusinessName = value; break;
                case "businesstype":
                    state.BusinessType = ValidBusinessTypes.FirstOrDefault(t => t.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? value;
                    break;
                case "city": state.City = value; break;
                case "ownername": state.OwnerName = value; break;
                default: return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to detect correction");
            return false;
        }
    }

    private async Task<string?> AskClaude(string prompt)
    {
        var client = _httpFactory.CreateClient("Claude");
        var body = new
        {
            model = "claude-sonnet-4-6",
            max_tokens = 100,
            messages = new[] { new { role = "user", content = prompt } }
        };
        var json = JsonSerializer.Serialize(body);
        var response = await client.PostAsync("/v1/messages", new StringContent(json, Encoding.UTF8, "application/json"));
        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);
        return doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString();
    }

    private static string CleanName(string raw)
    {
        var parts = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return raw.Trim();
        return string.Join(" ", parts.Select(p => p.Length > 0 ? char.ToUpper(p[0]) + (p.Length > 1 ? p[1..] : "") : p));
    }

    private static string GeneratePassword()
    {
        const string chars = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";
        return new string(Enumerable.Range(0, 12)
            .Select(_ => chars[System.Security.Cryptography.RandomNumberGenerator.GetInt32(chars.Length)])
            .ToArray());
    }

    private async Task Send(string to, string text) => await _whatsApp.SendMessageAsync(to, text);
}
