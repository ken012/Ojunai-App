using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ojunai.API.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscribedPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SubscribedPlan",
                table: "Businesses",
                type: "text",
                nullable: true);

            // Existing businesses without a trial are paid subscribers
            migrationBuilder.Sql("UPDATE \"Businesses\" SET \"SubscribedPlan\" = \"Plan\" WHERE \"TrialEndsAt\" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SubscribedPlan",
                table: "Businesses");
        }
    }
}
