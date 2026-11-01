using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SeedPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "plant_types",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "text", nullable: true),
                    nutrient_demand = table.Column<string>(type: "text", nullable: true),
                    germination_type = table.Column<string>(type: "text", nullable: true),
                    growth_days_min = table.Column<int>(type: "integer", nullable: true),
                    growth_days_max = table.Column<int>(type: "integer", nullable: true),
                    demand_per_person_min = table.Column<decimal>(type: "numeric", nullable: true),
                    demand_per_person_max = table.Column<decimal>(type: "numeric", nullable: true),
                    demand_unit = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plant_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plants",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    plant_type_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    seed_type = table.Column<string>(type: "text", nullable: false),
                    stock_status = table.Column<string>(type: "text", nullable: false),
                    cultivation_method = table.Column<string>(type: "text", nullable: true),
                    exposure = table.Column<string>(type: "text", nullable: false),
                    germination_days_min = table.Column<int>(type: "integer", nullable: true),
                    germination_days_max = table.Column<int>(type: "integer", nullable: true),
                    maturity_days_min = table.Column<int>(type: "integer", nullable: true),
                    maturity_days_max = table.Column<int>(type: "integer", nullable: true),
                    row_spacing_cm = table.Column<int>(type: "integer", nullable: true),
                    plant_spacing_cm = table.Column<int>(type: "integer", nullable: true),
                    pot_size_min_cm = table.Column<int>(type: "integer", nullable: true),
                    pot_size_max_cm = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plants", x => x.id);
                    table.ForeignKey(
                        name: "fk_plants_plant_types_plant_type_id",
                        column: x => x.plant_type_id,
                        principalTable: "plant_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "season_windows",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    plant_id = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    start_month = table.Column<int>(type: "integer", nullable: false),
                    end_month = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_season_windows", x => x.id);
                    table.ForeignKey(
                        name: "fk_season_windows_plants_plant_id",
                        column: x => x.plant_id,
                        principalTable: "plants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_plant_types_name",
                table: "plant_types",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plants_plant_type_id_name",
                table: "plants",
                columns: new[] { "plant_type_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_season_windows_plant_id",
                table: "season_windows",
                column: "plant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "season_windows");

            migrationBuilder.DropTable(
                name: "plants");

            migrationBuilder.DropTable(
                name: "plant_types");
        }
    }
}
