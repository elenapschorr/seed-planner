using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SeedPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlotSplits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cut_line_json",
                table: "plots",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "split_group",
                table: "plot_zones",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "split_line_json",
                table: "plot_zones",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "split_parent_json",
                table: "plot_zones",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "split_part",
                table: "plot_zones",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cut_line_json",
                table: "plots");

            migrationBuilder.DropColumn(
                name: "split_group",
                table: "plot_zones");

            migrationBuilder.DropColumn(
                name: "split_line_json",
                table: "plot_zones");

            migrationBuilder.DropColumn(
                name: "split_parent_json",
                table: "plot_zones");

            migrationBuilder.DropColumn(
                name: "split_part",
                table: "plot_zones");
        }
    }
}
