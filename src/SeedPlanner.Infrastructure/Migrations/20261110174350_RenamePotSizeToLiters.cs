using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SeedPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenamePotSizeToLiters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "pot_size_min_cm",
                table: "plants",
                newName: "pot_size_min_liters");

            migrationBuilder.RenameColumn(
                name: "pot_size_max_cm",
                table: "plants",
                newName: "pot_size_max_liters");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "pot_size_min_liters",
                table: "plants",
                newName: "pot_size_min_cm");

            migrationBuilder.RenameColumn(
                name: "pot_size_max_liters",
                table: "plants",
                newName: "pot_size_max_cm");
        }
    }
}
