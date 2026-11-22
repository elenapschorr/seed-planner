using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SeedPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskProgressToPlanLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "harvest_done_on",
                table: "plan_lines",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "harvested_plants",
                table: "plan_lines",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "planted_out_on",
                table: "plan_lines",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "sown_on",
                table: "plan_lines",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "harvest_done_on",
                table: "plan_lines");

            migrationBuilder.DropColumn(
                name: "harvested_plants",
                table: "plan_lines");

            migrationBuilder.DropColumn(
                name: "planted_out_on",
                table: "plan_lines");

            migrationBuilder.DropColumn(
                name: "sown_on",
                table: "plan_lines");
        }
    }
}
