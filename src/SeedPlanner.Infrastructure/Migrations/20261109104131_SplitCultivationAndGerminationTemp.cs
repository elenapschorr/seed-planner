using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SeedPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SplitCultivationAndGerminationTemp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "cultivation_method",
                table: "plants",
                newName: "sowing_method");

            migrationBuilder.AddColumn<string>(
                name: "growing_place",
                table: "plants",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "growing_place",
                table: "plants");

            migrationBuilder.RenameColumn(
                name: "sowing_method",
                table: "plants",
                newName: "cultivation_method");
        }
    }
}
