using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SeedPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlantHeight : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "height_evidence",
                table: "plants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "height_max_cm",
                table: "plants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "height_min_cm",
                table: "plants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "vine_length_max_cm",
                table: "plants",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "height_evidence",
                table: "plants");

            migrationBuilder.DropColumn(
                name: "height_max_cm",
                table: "plants");

            migrationBuilder.DropColumn(
                name: "height_min_cm",
                table: "plants");

            migrationBuilder.DropColumn(
                name: "vine_length_max_cm",
                table: "plants");
        }
    }
}
