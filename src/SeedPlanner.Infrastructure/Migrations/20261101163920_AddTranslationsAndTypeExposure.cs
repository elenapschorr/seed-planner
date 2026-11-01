using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SeedPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTranslationsAndTypeExposure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "exposure",
                table: "plants",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "exposure",
                table: "plant_types",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "plant_type_translations",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    plant_type_id = table.Column<int>(type: "integer", nullable: false),
                    language = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plant_type_translations", x => x.id);
                    table.ForeignKey(
                        name: "fk_plant_type_translations_plant_types_plant_type_id",
                        column: x => x.plant_type_id,
                        principalTable: "plant_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_plant_type_translations_plant_type_id_language",
                table: "plant_type_translations",
                columns: new[] { "plant_type_id", "language" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "plant_type_translations");

            migrationBuilder.DropColumn(
                name: "exposure",
                table: "plant_types");

            migrationBuilder.AlterColumn<string>(
                name: "exposure",
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
