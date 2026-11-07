using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SeedPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlantTypeCompanions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "plant_type_companions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    plant_type_id = table.Column<int>(type: "integer", nullable: false),
                    other_plant_type_id = table.Column<int>(type: "integer", nullable: false),
                    relation = table.Column<string>(type: "text", nullable: false),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plant_type_companions", x => x.id);
                    table.CheckConstraint("ck_plant_type_companions_order", "plant_type_id < other_plant_type_id");
                    table.ForeignKey(
                        name: "fk_plant_type_companions_plant_types_other_plant_type_id",
                        column: x => x.other_plant_type_id,
                        principalTable: "plant_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_plant_type_companions_plant_types_plant_type_id",
                        column: x => x.plant_type_id,
                        principalTable: "plant_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_plant_type_companions_other_plant_type_id",
                table: "plant_type_companions",
                column: "other_plant_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_plant_type_companions_plant_type_id_other_plant_type_id",
                table: "plant_type_companions",
                columns: new[] { "plant_type_id", "other_plant_type_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "plant_type_companions");
        }
    }
}
