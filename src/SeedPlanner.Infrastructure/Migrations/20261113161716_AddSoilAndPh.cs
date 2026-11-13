using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SeedPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSoilAndPh : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "soil_ph",
                table: "raised_beds",
                type: "numeric(3,1)",
                precision: 3,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "soil_type",
                table: "raised_beds",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "soil_ph",
                table: "pot_groups",
                type: "numeric(3,1)",
                precision: 3,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "soil_type",
                table: "pot_groups",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ph_max",
                table: "plant_types",
                type: "numeric(3,1)",
                precision: 3,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ph_min",
                table: "plant_types",
                type: "numeric(3,1)",
                precision: 3,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "soil_type",
                table: "plant_types",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "soil_ph",
                table: "raised_beds");

            migrationBuilder.DropColumn(
                name: "soil_type",
                table: "raised_beds");

            migrationBuilder.DropColumn(
                name: "soil_ph",
                table: "pot_groups");

            migrationBuilder.DropColumn(
                name: "soil_type",
                table: "pot_groups");

            migrationBuilder.DropColumn(
                name: "ph_max",
                table: "plant_types");

            migrationBuilder.DropColumn(
                name: "ph_min",
                table: "plant_types");

            migrationBuilder.DropColumn(
                name: "soil_type",
                table: "plant_types");
        }
    }
}
