using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ojunai.API.Migrations
{
    /// <summary>
    /// Baseline reconcile for two <c>Users</c> columns — <c>IntendedPlan</c> and
    /// <c>OnboardingInventoryCount</c>. Both properties were added to the model in commit 22530eb, which
    /// updated <c>User.cs</c> and <c>AppDbContextModelSnapshot.cs</c> but shipped NO migration, so no
    /// migration in the chain ever creates them. Because the snapshot already declared them,
    /// <c>dotnet ef migrations add</c> reports "no changes" and would never generate them: the gap was
    /// invisible to the tooling and self-perpetuating.
    ///
    /// Prod already has these columns (added out-of-band, the same way the Pricing V2 tables were — see
    /// <c>20260514000000_CreatePricingV2Tables</c>), so prod is healthy. But any FRESH database built from
    /// the migration chain lacks them, and every query that materialises a User entity then dies with
    /// <c>42703: column u.&lt;name&gt; does not exist</c> — which takes out the trial-reminder,
    /// daily/weekly-summary, renewal-reminder, daily-nudge and payment-reconciliation jobs, and any other
    /// path that loads a User (including the inbound bot handler).
    ///
    /// Written as <c>ADD COLUMN IF NOT EXISTS</c> rather than the generated <c>AddColumn</c> so it is a
    /// guaranteed no-op wherever the columns already exist (verified against a DB that has them: Postgres
    /// emits a NOTICE and skips) while repairing local and fresh builds. <c>OnboardingInventoryCount</c> is
    /// a non-nullable int defaulting to 0, so it carries NOT NULL DEFAULT 0 and back-fills existing rows.
    /// Down is deliberately a no-op: the columns predate this migration on prod, so a rollback must not
    /// drop them.
    /// </summary>
    public partial class AddUserModelDriftReconcile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""Users"" ADD COLUMN IF NOT EXISTS ""IntendedPlan"" text;");
            migrationBuilder.Sql(@"ALTER TABLE ""Users"" ADD COLUMN IF NOT EXISTS ""OnboardingInventoryCount"" integer NOT NULL DEFAULT 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty — see the class remarks. The columns predate this migration on prod,
            // so a rollback must not drop them.
        }
    }
}
