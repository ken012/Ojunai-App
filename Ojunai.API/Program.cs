using System.Text;
using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.Jobs;
using Ojunai.API.Services;
using Ojunai.API.Services.Interfaces;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ── Validate required configuration ──────────────────────────────────────────
var requiredKeys = new[] { "ConnectionStrings:DefaultConnection", "Jwt:Secret", "Twilio:AccountSid" };
foreach (var key in requiredKeys)
{
    if (string.IsNullOrEmpty(config[key]))
        throw new InvalidOperationException($"Required configuration '{key}' is not set. Check environment variables.");
}

// ── Database ─────────────────────────────────────────────────────────────────
var connString = config.GetConnectionString("DefaultConnection")!;
if (!connString.Contains("Maximum Pool Size", StringComparison.OrdinalIgnoreCase))
    connString += ";Maximum Pool Size=50;Minimum Pool Size=5";
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connString));

// ── JWT Authentication ────────────────────────────────────────────────────────
var jwtSecret = config["Jwt:Secret"]
    ?? throw new InvalidOperationException("JWT Secret is not configured.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = config["Jwt:Issuer"],
            ValidAudience            = config["Jwt:Audience"],
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew                = TimeSpan.Zero
        };
        options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                // Don't read cookie for admin/webhook/health endpoints — they use their own auth
                var path = context.Request.Path.Value ?? "";
                if (path.StartsWith("/api/admin") || path.StartsWith("/api/subscription/webhook") || path == "/health")
                    return Task.CompletedTask;

                if (string.IsNullOrEmpty(context.Token)
                    && context.Request.Cookies.TryGetValue("oj_auth", out var cookieToken)
                    && !string.IsNullOrEmpty(cookieToken))
                {
                    context.Token = cookieToken;
                }
                return Task.CompletedTask;
            },
            OnAuthenticationFailed = context =>
            {
                context.NoResult();
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// ── HTTP Clients ──────────────────────────────────────────────────────────────
builder.Services.AddHttpClient("Claude", client =>
{
    client.BaseAddress = new Uri("https://api.anthropic.com");
    client.DefaultRequestHeaders.Add("x-api-key", config["Claude:ApiKey"]);
    client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
    client.Timeout = TimeSpan.FromSeconds(30);
});


builder.Services.AddHttpClient("Paystack", client =>
{
    client.BaseAddress = new Uri("https://api.paystack.co");
    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {config["Paystack:SecretKey"]}");
    client.Timeout = TimeSpan.FromSeconds(30);
});


// ── Application Services ──────────────────────────────────────────────────────
builder.Services.AddHttpClient("VoiceAI", client =>
{
    client.BaseAddress = new Uri(config["VoiceAI:BaseUrl"] ?? "https://voice.ojunai.com");
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddScoped<PaystackService>();
builder.Services.AddScoped<FlutterwaveService>();
builder.Services.AddScoped<PlanGuard>();
builder.Services.AddScoped<VoiceAIGuard>();
builder.Services.AddScoped<VoiceAIProvisioningService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IPhoneVerificationService, PhoneVerificationService>();
builder.Services.AddScoped<IEmailVerificationService, EmailVerificationService>();
builder.Services.AddScoped<IAccountRecoveryService, AccountRecoveryService>();
builder.Services.AddScoped<IBackgroundImageService, BackgroundImageService>();
builder.Services.AddScoped<IAlertService, AlertService>();
builder.Services.AddScoped<AlertGeneratorJobService>();
builder.Services.AddScoped<IBusinessService, BusinessService>();
builder.Services.AddScoped<IStockHoldService, StockHoldService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<ISalesService, SalesService>();
builder.Services.AddScoped<IExpenseService, ExpenseService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IContactService, ContactService>();
builder.Services.AddScoped<ILedgerService, LedgerService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IPdfExportService, PdfExportService>();
builder.Services.AddScoped<IReceiptService, ReceiptService>();
builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddScoped<IWhatsAppService, WhatsAppService>();
builder.Services.AddScoped<IClaudeParsingService, ClaudeParsingService>();
builder.Services.AddScoped<IEntityResolverService, EntityResolverService>();

// ── Phase-1 channel abstraction ────────────────────────────────────────────────
// Adapters are registered as IChannelAdapter; ChannelRegistry collects them all by
// IEnumerable<IChannelAdapter> at construction. Adding a new channel is one new adapter
// + one DI line here.
builder.Services.AddScoped<Ojunai.API.Services.Channels.IChannelAdapter, Ojunai.API.Services.Channels.TwilioWhatsAppAdapter>();
builder.Services.AddScoped<Ojunai.API.Services.Channels.IChannelAdapter, Ojunai.API.Services.Channels.Telegram.TelegramAdapter>();
builder.Services.AddScoped<Ojunai.API.Services.Channels.IChannelAdapter, Ojunai.API.Services.Channels.Messenger.MessengerAdapter>();
builder.Services.AddScoped<Ojunai.API.Services.Channels.Telegram.TelegramAdapter>(); // For direct injection (PDF send extension)
builder.Services.AddScoped<Ojunai.API.Services.Channels.Messenger.MessengerAdapter>(); // For direct injection by MessengerIntentHandler
builder.Services.AddScoped<Ojunai.API.Services.Channels.IChannelRegistry, Ojunai.API.Services.Channels.ChannelRegistry>();
builder.Services.AddScoped<Ojunai.API.Services.Channels.IChannelLinkingService, Ojunai.API.Services.Channels.ChannelLinkingService>();
builder.Services.AddScoped<Ojunai.API.Services.Channels.IChatQueryService, Ojunai.API.Services.Channels.ChatQueryService>();
builder.Services.AddScoped<Ojunai.API.Services.Channels.INotificationDispatcher, Ojunai.API.Services.Channels.NotificationDispatcher>();
builder.Services.AddScoped<Ojunai.API.Services.Channels.Telegram.IPendingTelegramActionService, Ojunai.API.Services.Channels.Telegram.PendingTelegramActionService>();
builder.Services.AddScoped<Ojunai.API.Services.Channels.Telegram.ITelegramIntentHandler, Ojunai.API.Services.Channels.Telegram.TelegramIntentHandler>();
builder.Services.AddScoped<Ojunai.API.Services.Channels.Messenger.IMessengerIntentHandler, Ojunai.API.Services.Channels.Messenger.MessengerIntentHandler>();
builder.Services.AddScoped<Ojunai.API.Services.Channels.IConversationOrchestrator, Ojunai.API.Services.Channels.ConversationOrchestrator>();
builder.Services.AddScoped<OnboardingService>();
builder.Services.AddScoped<SummaryJobService>();
builder.Services.AddScoped<TrialReminderJobService>();
builder.Services.AddScoped<TrialRevertJobService>();
builder.Services.AddScoped<VoiceAITrialRevertJobService>();
builder.Services.AddScoped<RenewalReminderJobService>();
builder.Services.AddScoped<ImportJobService>();
builder.Services.AddScoped<PaymentReconciliationJobService>();

// ── Hangfire ──────────────────────────────────────────────────────────────────
builder.Services.AddHangfire(hf => hf
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(c =>
        c.UseNpgsqlConnection(config.GetConnectionString("DefaultConnection"))));

builder.Services.AddHangfireServer();

// ── CORS ──────────────────────────────────────────────────────────────────────
var corsOrigins = (config["Cors:AllowedOrigins"] ?? "http://localhost:3000,http://localhost:3001")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
{
    options.AddPolicy("DashboardPolicy", policy =>
        policy
            .WithOrigins(corsOrigins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials());
});

// ── Controllers ───────────────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        opts.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        opts.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    })
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = ctx =>
        {
            var errors = ctx.ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();
            return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(
                ApiResponse<object>.Fail(errors));
        };
    });

// ── Swagger ───────────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title   = "Ojunai API",
        Version = "v1",
        Description = "WhatsApp-first AI business operator for African SMEs"
    });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name         = "Authorization",
        Type         = SecuritySchemeType.Http,
        Scheme       = "Bearer",
        BearerFormat = "JWT",
        In           = ParameterLocation.Header
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// ── Build ─────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── Forwarded Headers ─────────────────────────────────────────────────────────
// Production runs behind Nginx (or any reverse proxy). Without this, every request
// appears to come from 127.0.0.1, breaking the per-IP rate limiter on auth endpoints.
// We restrict the trust list to loopback so an attacker can't spoof X-Forwarded-For
// from outside the reverse proxy.
var forwardedOptions = new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                     | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto,
    ForwardLimit = 1,
};
forwardedOptions.KnownProxies.Clear();
forwardedOptions.KnownNetworks.Clear();
forwardedOptions.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(System.Net.IPAddress.Loopback, 8));
forwardedOptions.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(System.Net.IPAddress.IPv6Loopback, 128));
app.UseForwardedHeaders(forwardedOptions);

// ── Global Exception Handler ──────────────────────────────────────────────────
app.UseExceptionHandler(errApp =>
{
    errApp.Run(async ctx =>
    {
        ctx.Response.ContentType = "application/json";
        var error = ctx.Features.Get<IExceptionHandlerFeature>();

        string resultJson;
        if (error?.Error is UnauthorizedAccessException)
        {
            ctx.Response.StatusCode = 401;
            resultJson = JsonSerializer.Serialize(ApiResponse<object>.Fail(error.Error.Message));
        }
        else if (error?.Error is InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            ctx.Response.StatusCode = 400;
            resultJson = JsonSerializer.Serialize(ApiResponse<object>.Fail(error.Error.Message));
        }
        else
        {
            ctx.Response.StatusCode = 500;
            if (error != null)
            {
                var logger = ctx.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogError(error.Error, "Unhandled exception");
            }
            resultJson = JsonSerializer.Serialize(ApiResponse<object>.Fail("An unexpected error occurred."));
        }

        await ctx.Response.WriteAsync(resultJson);
    });
});

// ── Middleware Pipeline ───────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Security headers applied to all responses. These mitigate clickjacking, MIME sniffing,
// information leakage via referrers, and (in production) enforce HTTPS via HSTS.
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
    if (app.Environment.IsProduction())
    {
        context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
    }
    await next();
});

app.UseCors("DashboardPolicy");

// Enable body buffering for webhook signature verification (Twilio, Paystack need to re-read raw body).
app.Use(async (context, next) =>
{
    context.Request.EnableBuffering();
    await next();
});

// Routing must run before our middleware so it can inspect endpoint metadata
// (e.g. [AllowAnonymous]) — without this, the middleware can't tell which
// requests are recovery endpoints (login, password reset) and ends up blocking
// users who are trying to log in with a stale cookie.
app.UseRouting();

app.UseAuthentication();
app.UseMiddleware<ActiveUserMiddleware>();
app.UseAuthorization();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireLocalAuthFilter() }
});

app.MapControllers();

app.MapMethods("/health", new[] { "GET", "HEAD" }, async (AppDbContext db) =>
{
    try
    {
        await db.Database.CanConnectAsync();
        return Results.Ok(new { status = "ok", database = "connected" });
    }
    catch
    {
        return Results.Json(new { status = "degraded", database = "disconnected" }, statusCode: 503);
    }
}).AllowAnonymous();

// ── Run Migrations on Startup ─────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }
    catch (Exception ex)
    {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Migration failed on startup");
    }
}

// ── Hangfire Recurring Jobs ───────────────────────────────────────────────────
var lagosZone  = TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos");

RecurringJob.AddOrUpdate<SummaryJobService>(
    "daily-summary",
    svc => svc.RunDailySummaryAsync(),
    "0 * * * *");

RecurringJob.AddOrUpdate<SummaryJobService>(
    "weekly-summary",
    svc => svc.RunWeeklySummaryAsync(),
    "0 * * * *");

RecurringJob.AddOrUpdate<TrialReminderJobService>(
    "trial-reminders",
    svc => svc.SendTrialRemindersAsync(),
    "0 * * * *");

// Dashboard-bell alert generator. Hourly tick handles aged-receivable scan + trial-ending +
// per-timezone daily-summary fan-out. Each alert is dedup'd internally so re-running is safe.
RecurringJob.AddOrUpdate<AlertGeneratorJobService>(
    "dashboard-alerts",
    svc => svc.RunAsync(),
    "0 * * * *");

RecurringJob.AddOrUpdate<TrialRevertJobService>(
    "trial-revert",
    svc => svc.RevertExpiredTrialsAsync(),
    "0 */4 * * *",
    new RecurringJobOptions { TimeZone = lagosZone });

RecurringJob.AddOrUpdate<VoiceAITrialRevertJobService>(
    "voiceai-trial-revert",
    svc => svc.RevertExpiredTrialsAsync(),
    "0 */4 * * *",
    new RecurringJobOptions { TimeZone = lagosZone });

RecurringJob.AddOrUpdate<RenewalReminderJobService>(
    "renewal-reminders",
    svc => svc.SendRenewalRemindersAsync(),
    "0 * * * *");

RecurringJob.AddOrUpdate<PaymentReconciliationJobService>(
    "payment-reconciliation",
    svc => svc.ReconcileAsync(),
    "0 6 * * *");

app.Run();
