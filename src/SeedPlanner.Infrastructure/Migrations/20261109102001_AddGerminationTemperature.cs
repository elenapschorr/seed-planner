using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SeedPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGerminationTemperature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "germination_temp_max_c",
                table: "plants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "germination_temp_min_c",
                table: "plants",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "germination_temp_max_c",
                table: "plants");

            migrationBuilder.DropColumn(
                name: "germination_temp_min_c",
                table: "plants");
        }
    }
}
