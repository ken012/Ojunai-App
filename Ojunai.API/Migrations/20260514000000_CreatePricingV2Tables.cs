using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ojunai.API.Migrations
{
    /// <summary>
    /// Baseline reconcile for the Pricing V2 core tables — Subscriptions, BusinessAddOns,
    /// ActionUsages, BusinessOverrides. These were created on prod via a manual SQL hotfix
    /// (the original <c>20260509055131_PricingV2Foundation</c> migration only added the
    /// <c>Users.IntendedBillingPeriod</c> column and never created the tables), so no migration
    /// in the chain actually builds them. A fresh <c>dotnet ef database update</c> would fail:
    /// the June migrations <c>AddActionUsageChannelCounts</c> and <c>AddPackAutoRenewFields</c>
    /// ALTER these tables, hitting "relation does not exist".
    ///
    /// This migration sits AFTER <c>BaselineSnapshotResync</c> and BEFORE those June column-adds,
    /// so on a fresh database the tables exist before anything alters them. Every statement is
    /// <c>IF NOT EXISTS</c>, so running it against a box that already has the tables is a no-op.
    ///
    /// Tables are created in their MAY shape — the columns added by later migrations are
    /// deliberately omitted so those migrations apply cleanly:
    ///   • ActionUsages   — no MessagingCount / WhatsAppCount  (added 20260601033942)
    ///   • BusinessAddOns — no IsAutoRenew / ProviderSubscriptionId + its index (added 20260602033750)
    ///
    /// On prod (where the tables already exist) this migration should be marked applied via
    /// <c>INSERT INTO "__EFMigrationsHistory"</c> alongside the deploy, matching how
    /// <c>BaselineSnapshotResync</c> was handled — the IF NOT EXISTS guards make an accidental
    /// run harmless either way. The body only matters for fresh-DB rebuilds.
    /// </summary>
    public partial class CreatePricingV2Tables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Tables ─────────────────────────────────────────────────────────────

            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""Subscriptions"" (
                    ""Id"" uuid NOT NULL,
                    ""BillingCurrency"" character varying(10) NOT NULL,
                    ""BillingCycle"" character varying(10) NOT NULL,
                    ""BusinessId"" uuid NOT NULL,
                    ""CancelledAtUtc"" timestamp with time zone,
                    ""CreatedAtUtc"" timestamp with time zone NOT NULL,
                    ""CurrentPeriodEndsAtUtc"" timestamp with time zone,
                    ""CurrentPeriodStartsAtUtc"" timestamp with time zone,
                    ""GraceDays"" integer NOT NULL,
                    ""IsAutoRenew"" boolean NOT NULL,
                    ""ProductLine"" integer NOT NULL,
                    ""Provider"" character varying(40) NOT NULL,
                    ""ProviderSubscriptionId"" character varying(200),
                    ""StartedAtUtc"" timestamp with time zone NOT NULL,
                    ""Status"" character varying(20) NOT NULL,
                    ""Tier"" character varying(40) NOT NULL,
                    ""TrialEndsAtUtc"" timestamp with time zone,
                    ""UpdatedAtUtc"" timestamp with time zone NOT NULL,
                    CONSTRAINT ""PK_Subscriptions"" PRIMARY KEY (""Id"")
                );
            ");

            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""BusinessAddOns"" (
                    ""Id"" uuid NOT NULL,
                    ""AddOnCode"" character varying(60) NOT NULL,
                    ""AddedAtUtc"" timestamp with time zone NOT NULL,
                    ""BilledAmount"" numeric NOT NULL,
                    ""BilledCurrency"" character varying(10) NOT NULL,
                    ""BusinessId"" uuid NOT NULL,
                    ""CancelledAtUtc"" timestamp with time zone,
                    ""NextBillingAtUtc"" timestamp with time zone,
                    ""Quantity"" integer NOT NULL,
                    ""Status"" character varying(20) NOT NULL,
                    ""UpdatedAtUtc"" timestamp with time zone NOT NULL,
                    CONSTRAINT ""PK_BusinessAddOns"" PRIMARY KEY (""Id"")
                );
            ");

            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""ActionUsages"" (
                    ""BusinessId"" uuid NOT NULL,
                    ""ProductLine"" integer NOT NULL,
                    ""PeriodStartUtc"" timestamp with time zone NOT NULL,
                    ""Count"" integer NOT NULL,
                    ""LastIncrementedAtUtc"" timestamp with time zone NOT NULL,
                    CONSTRAINT ""PK_ActionUsages"" PRIMARY KEY (""BusinessId"", ""ProductLine"", ""PeriodStartUtc"")
                );
            ");

            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""BusinessOverrides"" (
                    ""Id"" uuid NOT NULL,
                    ""BusinessId"" uuid NOT NULL,
                    ""CreatedAtUtc"" timestamp with time zone NOT NULL,
                    ""CreatedByUserId"" uuid NOT NULL,
                    ""ExpiresAtUtc"" timestamp with time zone,
                    ""LegacyPriceAmount"" numeric,
                    ""LegacyPriceCurrency"" character varying(10),
                    ""LegacyTier"" character varying(40),
                    ""OverrideType"" character varying(40) NOT NULL,
                    ""ReasonNote"" character varying(500),
                    ""RevokedAtUtc"" timestamp with time zone,
                    CONSTRAINT ""PK_BusinessOverrides"" PRIMARY KEY (""Id"")
                );
            ");

            // ── Indexes ────────────────────────────────────────────────────────────

            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_Subscriptions_ProviderSubscriptionId"" ON ""Subscriptions"" (""ProviderSubscriptionId"");");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_Subscriptions_BusinessId_ProductLine_Status"" ON ""Subscriptions"" (""BusinessId"", ""ProductLine"", ""Status"");");

            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_BusinessAddOns_BusinessId_Status"" ON ""BusinessAddOns"" (""BusinessId"", ""Status"");");

            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_BusinessOverrides_ExpiresAtUtc"" ON ""BusinessOverrides"" (""ExpiresAtUtc"");");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_BusinessOverrides_BusinessId_OverrideType"" ON ""BusinessOverrides"" (""BusinessId"", ""OverrideType"");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down() exists for tooling completeness but should never run on prod — these tables
            // were created OUTSIDE of EF and hold real billing data; dropping them via a Down()
            // would wipe it. Left empty on purpose, matching BaselineSnapshotResync.
        }
    }
}
