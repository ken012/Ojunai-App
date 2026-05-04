using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ojunai.API.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessAccountNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Add column as nullable first so existing rows don't fail
            migrationBuilder.AddColumn<string>(
                name: "AccountNumber",
                table: "Businesses",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            // 2. Backfill existing businesses with unique random 7-digit numbers
            migrationBuilder.Sql(@"
                DO $$
                DECLARE
                    biz RECORD;
                    new_num TEXT;
                BEGIN
                    FOR biz IN SELECT ""Id"" FROM ""Businesses"" WHERE ""AccountNumber"" IS NULL OR ""AccountNumber"" = '' LOOP
                        LOOP
                            new_num := floor(random() * 9000000 + 1000000)::text;
                            EXIT WHEN NOT EXISTS (SELECT 1 FROM ""Businesses"" WHERE ""AccountNumber"" = new_num);
                        END LOOP;
                        UPDATE ""Businesses"" SET ""AccountNumber"" = new_num WHERE ""Id"" = biz.""Id"";
                    END LOOP;
                END $$;
            ");

            // 3. Make non-nullable now that all rows have values
            migrationBuilder.AlterColumn<string>(
                name: "AccountNumber",
                table: "Businesses",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            // 4. Create unique index
            migrationBuilder.CreateIndex(
                name: "IX_Businesses_AccountNumber",
                table: "Businesses",
                column: "AccountNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Businesses_AccountNumber",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "AccountNumber",
                table: "Businesses");
        }
    }
}
