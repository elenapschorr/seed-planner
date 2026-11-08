using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SeedPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLifecycleAndPropagation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "seed_type",
                table: "plants",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "propagation",
                table: "plants",
                type: "text",
                nullable: false,
                defaultValue: "Seed");

            migrationBuilder.AddColumn<string>(
                name: "lifecycle",
                table: "plant_types",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "propagation",
                table: "plants");

            migrationBuilder.DropColumn(
                name: "lifecycle",
                table: "plant_types");

            migrationBuilder.AlterColumn<string>(
                name: "seed_type",
                table: "plants",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
