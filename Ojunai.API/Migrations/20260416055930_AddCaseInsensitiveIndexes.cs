using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ojunai.API.Migrations
{
    /// <summary>
    /// Adds functional indexes on LOWER(Name) for Products and Contacts. Existing queries call
    /// <c>.ToLower()</c> on both sides of comparisons (EF translates to Postgres LOWER()), which
    /// defeats the regular btree indexes on those columns. These functional indexes restore index
    /// usage for case-insensitive lookups without requiring any code changes.
    /// </summary>
    public partial class AddCaseInsensitiveIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_Products_BusinessId_NameLower\" " +
                "ON \"Products\" (\"BusinessId\", LOWER(\"Name\"));");

            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_Contacts_BusinessId_NameLower\" " +
                "ON \"Contacts\" (\"BusinessId\", LOWER(\"Name\"));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Products_BusinessId_NameLower\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Contacts_BusinessId_NameLower\";");
        }
    }
}
