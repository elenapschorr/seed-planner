using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SeedPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlotZoneProperties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "placed_beds_json",
                table: "plots",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "cold_frame_possible",
                table: "plot_zones",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "slope",
                table: "plot_zones",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "soil",
                table: "plot_zones",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "sun",
                table: "plot_zones",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "use",
                table: "plot_zones",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "water_nearby",
                table: "plot_zones",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "waterlogged",
                table: "plot_zones",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "placed_beds_json",
                table: "plots");

            migrationBuilder.DropColumn(
                name: "cold_frame_possible",
                table: "plot_zones");

            migrationBuilder.DropColumn(
                name: "slope",
                table: "plot_zones");

            migrationBuilder.DropColumn(
                name: "soil",
                table: "plot_zones");

            migrationBuilder.DropColumn(
                name: "sun",
                table: "plot_zones");

            migrationBuilder.DropColumn(
                name: "use",
                table: "plot_zones");

            migrationBuilder.DropColumn(
                name: "water_nearby",
                table: "plot_zones");

            migrationBuilder.DropColumn(
                name: "waterlogged",
                table: "plot_zones");
        }
    }
}
