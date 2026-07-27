using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ojunai.API.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSelectedLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SelectedLocationId",
                table: "Users",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SelectedLocationId",
                table: "Users");
        }
    }
}
