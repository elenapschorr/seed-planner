using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SeedPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PlanValidityPeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_plans_owner_id_year_name",
                table: "plans");

            migrationBuilder.AddColumn<DateOnly>(
                name: "valid_from",
                table: "plans",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<DateOnly>(
                name: "valid_to",
                table: "plans",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            // Keep the data: a plan of a year is valid from 1 January to 31 December of that year
            migrationBuilder.Sql(
                "UPDATE plans SET valid_from = make_date(year, 1, 1), valid_to = make_date(year, 12, 31);");

            migrationBuilder.DropColumn(
                name: "year",
                table: "plans");

            migrationBuilder.CreateIndex(
                name: "ix_plans_owner_id_valid_from_name",
                table: "plans",
                columns: new[] { "owner_id", "valid_from", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_plans_owner_id_valid_from_name",
                table: "plans");

            migrationBuilder.AddColumn<int>(
                name: "year",
                table: "plans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("UPDATE plans SET year = EXTRACT(YEAR FROM valid_from)::int;");

            migrationBuilder.DropColumn(
                name: "valid_from",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "valid_to",
                table: "plans");

            migrationBuilder.CreateIndex(
                name: "ix_plans_owner_id_year_name",
                table: "plans",
                columns: new[] { "owner_id", "year", "name" },
                unique: true);
        }
    }
}
