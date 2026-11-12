using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SeedPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIndoorWeeks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "frost_sensitive",
                table: "plants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "indoor_weeks_max",
                table: "plants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "indoor_weeks_min",
                table: "plants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "last_frost_third",
                table: "plans",
                type: "integer",
                nullable: false,
                defaultValue: 13);

            migrationBuilder.AddCheckConstraint(
                name: "ck_plans_last_frost_third",
                table: "plans",
                sql: "last_frost_third BETWEEN 0 AND 35");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_plans_last_frost_third",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "frost_sensitive",
                table: "plants");

            migrationBuilder.DropColumn(
                name: "indoor_weeks_max",
                table: "plants");

            migrationBuilder.DropColumn(
                name: "indoor_weeks_min",
                table: "plants");

            migrationBuilder.DropColumn(
                name: "last_frost_third",
                table: "plans");
        }
    }
}
