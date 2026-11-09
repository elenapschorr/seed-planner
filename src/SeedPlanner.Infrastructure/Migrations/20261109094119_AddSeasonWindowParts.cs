using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SeedPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSeasonWindowParts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "end_part",
                table: "season_windows",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Whole");

            migrationBuilder.AddColumn<string>(
                name: "start_part",
                table: "season_windows",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Whole");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "end_part",
                table: "season_windows");

            migrationBuilder.DropColumn(
                name: "start_part",
                table: "season_windows");
        }
    }
}
