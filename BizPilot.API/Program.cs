using System.Text;
using BizPilot.API.Common;
using BizPilot.API.Data;
using BizPilot.API.Jobs;
using BizPilot.API.Services;
using BizPilot.API.Services.Interfaces;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ── Database ─────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(config.GetConnectionString("DefaultConnection")));

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

builder.Services.AddTransient<FlutterwaveAuthHandler>();
builder.Services.AddHttpClient("Flutterwave", client =>
{
    client.BaseAddress = new Uri("https://api.flutterwave.com");
    client.Timeout = TimeSpan.FromSeconds(30);
}).AddHttpMessageHandler<FlutterwaveAuthHandler>();


// ── Application Services ──────────────────────────────────────────────────────
builder.Services.AddScoped<PaystackService>();
builder.Services.AddScoped<FlutterwaveService>();
builder.Services.AddScoped<PlanGuard>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IBusinessService, BusinessService>();
builder.Services.AddScoped<IStockHoldService, StockHoldService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<ISalesService, SalesService>();
builder.Services.AddScoped<IExpenseService, ExpenseService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IContactService, ContactService>();
builder.Services.AddScoped<ILedgerService, LedgerService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IWhatsAppService, WhatsAppService>();
builder.Services.AddScoped<IClaudeParsingService, ClaudeParsingService>();
builder.Services.AddScoped<OnboardingService>();
builder.Services.AddScoped<SummaryJobService>();
builder.Services.AddScoped<TrialReminderJobService>();
builder.Services.AddScoped<TrialRevertJobService>();
builder.Services.AddScoped<RenewalReminderJobService>();
builder.Services.AddScoped<ImportJobService>();

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
        Title   = "BizPilot AI API",
        Version = "v1",
        Description = "WhatsApp-first AI business operator for Nigerian SMEs"
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

app.UseAuthentication();
app.UseMiddleware<ActiveUserMiddleware>();
app.UseAuthorization();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireLocalAuthFilter() }
});

app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
   .AllowAnonymous();

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
var dailyCron  = config["Hangfire:DailySummaryCron"]  ?? "0 20 * * *";
var weeklyCron = config["Hangfire:WeeklySummaryCron"] ?? "0 8 * * 1";

RecurringJob.AddOrUpdate<SummaryJobService>(
    "daily-summary",
    svc => svc.RunDailySummaryAsync(),
    dailyCron,
    new RecurringJobOptions { TimeZone = lagosZone });

RecurringJob.AddOrUpdate<SummaryJobService>(
    "weekly-summary",
    svc => svc.RunWeeklySummaryAsync(),
    weeklyCron,
    new RecurringJobOptions { TimeZone = lagosZone });

RecurringJob.AddOrUpdate<TrialReminderJobService>(
    "trial-reminders",
    svc => svc.SendTrialRemindersAsync(),
    "0 10 * * *",
    new RecurringJobOptions { TimeZone = lagosZone });

RecurringJob.AddOrUpdate<TrialRevertJobService>(
    "trial-revert",
    svc => svc.RevertExpiredTrialsAsync(),
    "0 */4 * * *",
    new RecurringJobOptions { TimeZone = lagosZone });

RecurringJob.AddOrUpdate<RenewalReminderJobService>(
    "renewal-reminders",
    svc => svc.SendRenewalRemindersAsync(),
    "0 * * * *");

app.Run();
