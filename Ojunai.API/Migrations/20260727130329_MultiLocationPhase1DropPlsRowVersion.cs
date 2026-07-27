using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ojunai.API.Migrations
{
    /// <inheritdoc />
    public partial class MultiLocationPhase1DropPlsRowVersion : Migration
    {
        // Multi-location Phase 1 removed the optimistic-concurrency token from ProductLocationStock (it was
        // mapped to Postgres' `xmin` system column). EF's scaffolder emitted DropColumn("xmin")/AddColumn —
        // but `xmin` is a system column that CANNOT be dropped or added (the DDL would error). There is no
        // real column to change: dropping the rowversion is a MODEL-only change (EF simply stops emitting
        // `WHERE xmin = @p` on updates). So Up/Down are intentionally no-ops; the accompanying model-snapshot
        // update is the only meaningful part of this migration.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
