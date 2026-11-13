using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SeedPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanAmountOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "amount_override",
                table: "plan_items",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "amount_override",
                table: "plan_items");
        }
    }
}
