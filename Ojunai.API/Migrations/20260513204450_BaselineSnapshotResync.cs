using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ojunai.API.Migrations
{
    /// <summary>
    /// Baseline reconcile — captures schema that exists on prod but never had a corresponding
    /// migration file. Every statement is wrapped in IF NOT EXISTS so running this against a
    /// box that already has the tables/columns is a no-op, while running it against a fresh
    /// database creates everything correctly.
    ///
    /// On prod (where everything already exists) this migration was manually marked applied
    /// via INSERT INTO "__EFMigrationsHistory" — its Up() body is never actually executed
    /// there. The body matters for fresh-DB rebuilds: dotnet ef database update will produce
    /// a database whose schema matches the entity model.
    ///
    /// What's reconciled here:
    ///   • CreateTable: AdminAuditEntries, AdminMetricSnapshots, ChannelLinkTokens,
    ///                  PendingTelegramActions
    ///   • AddColumn:   Users.AlertChannel
    ///                  Businesses.LargeSaleAlert{WhatsApp,Telegram,Messenger,Dashboard}
    ///   • CreateIndex: matching index set for each table above.
    /// </summary>
    public partial class BaselineSnapshotResync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Tables ─────────────────────────────────────────────────────────────

            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""AdminAuditEntries"" (
                    ""Id"" uuid NOT NULL,
                    ""CreatedAtUtc"" timestamp with time zone NOT NULL,
                    ""Endpoint"" character varying(200) NOT NULL,
                    ""Ip"" character varying(64),
                    ""KeyPrefix"" character varying(24),
                    ""Success"" boolean NOT NULL,
                    ""StatusCode"" integer NOT NULL,
                    ""QueryString"" character varying(500),
                    CONSTRAINT ""PK_AdminAuditEntries"" PRIMARY KEY (""Id"")
                );
            ");

            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""AdminMetricSnapshots"" (
                    ""Id"" uuid NOT NULL,
                    ""CapturedAtUtc"" timestamp with time zone NOT NULL,
                    ""CapturedDate"" date NOT NULL,
                    ""MetricName"" character varying(40) NOT NULL,
                    ""ChannelFilter"" character varying(20),
                    ""Value"" numeric NOT NULL,
                    ""ValueText"" character varying(80),
                    CONSTRAINT ""PK_AdminMetricSnapshots"" PRIMARY KEY (""Id"")
                );
            ");

            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""ChannelLinkTokens"" (
                    ""Id"" uuid NOT NULL,
                    ""UserId"" uuid NOT NULL,
                    ""BusinessId"" uuid NOT NULL,
                    ""Channel"" integer NOT NULL,
                    ""Token"" character varying(80) NOT NULL,
                    ""ExpiresAtUtc"" timestamp with time zone NOT NULL,
                    ""CreatedAtUtc"" timestamp with time zone NOT NULL,
                    ""ConsumedAtUtc"" timestamp with time zone,
                    ""BoundToIdentity"" character varying(120),
                    CONSTRAINT ""PK_ChannelLinkTokens"" PRIMARY KEY (""Id"")
                );
            ");

            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""PendingTelegramActions"" (
                    ""Id"" uuid NOT NULL,
                    ""BusinessId"" uuid NOT NULL,
                    ""UserId"" uuid NOT NULL,
                    ""ChatId"" character varying(64) NOT NULL,
                    ""Token"" character varying(32) NOT NULL,
                    ""ActionType"" character varying(40) NOT NULL,
                    ""PayloadJson"" jsonb NOT NULL,
                    ""CreatedAtUtc"" timestamp with time zone NOT NULL,
                    ""ExpiresAtUtc"" timestamp with time zone NOT NULL,
                    ""ConsumedAtUtc"" timestamp with time zone,
                    CONSTRAINT ""PK_PendingTelegramActions"" PRIMARY KEY (""Id"")
                );
            ");

            // ── Indexes ────────────────────────────────────────────────────────────

            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_AdminAuditEntries_CreatedAtUtc"" ON ""AdminAuditEntries"" (""CreatedAtUtc"");");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_AdminAuditEntries_KeyPrefix"" ON ""AdminAuditEntries"" (""KeyPrefix"");");

            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_AdminMetricSnapshots_MetricName_CapturedDate"" ON ""AdminMetricSnapshots"" (""MetricName"", ""CapturedDate"");");
            migrationBuilder.Sql(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_AdminMetricSnapshots_MetricName_ChannelFilter_CapturedDate"" ON ""AdminMetricSnapshots"" (""MetricName"", ""ChannelFilter"", ""CapturedDate"");");

            migrationBuilder.Sql(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ChannelLinkTokens_Token"" ON ""ChannelLinkTokens"" (""Token"");");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_ChannelLinkTokens_ExpiresAtUtc"" ON ""ChannelLinkTokens"" (""ExpiresAtUtc"");");

            migrationBuilder.Sql(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PendingTelegramActions_Token"" ON ""PendingTelegramActions"" (""Token"");");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_PendingTelegramActions_ExpiresAtUtc"" ON ""PendingTelegramActions"" (""ExpiresAtUtc"");");

            // ── Columns on existing tables ─────────────────────────────────────────
            // ADD COLUMN has IF NOT EXISTS natively in Postgres 9.6+ — both prod and
            // any new dev box run Postgres 15+ so this is fine.

            migrationBuilder.Sql(@"ALTER TABLE ""Users"" ADD COLUMN IF NOT EXISTS ""AlertChannel"" text NOT NULL DEFAULT '';");

            migrationBuilder.Sql(@"ALTER TABLE ""Businesses"" ADD COLUMN IF NOT EXISTS ""LargeSaleAlertDashboard"" boolean NOT NULL DEFAULT false;");
            migrationBuilder.Sql(@"ALTER TABLE ""Businesses"" ADD COLUMN IF NOT EXISTS ""LargeSaleAlertMessenger"" boolean NOT NULL DEFAULT false;");
            migrationBuilder.Sql(@"ALTER TABLE ""Businesses"" ADD COLUMN IF NOT EXISTS ""LargeSaleAlertTelegram"" boolean NOT NULL DEFAULT false;");
            migrationBuilder.Sql(@"ALTER TABLE ""Businesses"" ADD COLUMN IF NOT EXISTS ""LargeSaleAlertWhatsApp"" boolean NOT NULL DEFAULT false;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down() exists for tooling completeness but should never run on prod — the
            // tables and columns this baseline describes were created OUTSIDE of EF and
            // dropping them via a Down() would wipe real data. Left empty on purpose.
        }
    }
}
