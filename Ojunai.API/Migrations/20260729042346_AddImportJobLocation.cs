using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ojunai.API.Migrations
{
    /// <inheritdoc />
    public partial class AddImportJobLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                table: "ImportJobs",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "ImportJobs");
        }
    }
}
