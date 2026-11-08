using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SeedPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPotPlantingsAndGroundBeds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_pot_uses_plant_types_plant_type_id",
                table: "pot_uses");

            migrationBuilder.DropCheckConstraint(
                name: "ck_raised_beds_measures",
                table: "raised_beds");

            migrationBuilder.DropIndex(
                name: "ix_pot_uses_plant_type_id",
                table: "pot_uses");

            migrationBuilder.DropColumn(
                name: "plant_type_id",
                table: "pot_uses");

            migrationBuilder.AlterColumn<decimal>(
                name: "height_cm",
                table: "raised_beds",
                type: "numeric(6,1)",
                precision: 6,
                scale: 1,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(6,1)",
                oldPrecision: 6,
                oldScale: 1);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "raised_beds",
                type: "text",
                nullable: false,
                defaultValue: "Raised");

            migrationBuilder.CreateTable(
                name: "bed_plantings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    raised_bed_id = table.Column<int>(type: "integer", nullable: false),
                    plant_type_id = table.Column<int>(type: "integer", nullable: false),
                    plant_id = table.Column<int>(type: "integer", nullable: true),
                    plant_count = table.Column<int>(type: "integer", nullable: true),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bed_plantings", x => x.id);
                    table.CheckConstraint("ck_bed_plantings_count", "plant_count IS NULL OR plant_count >= 1");
                    table.ForeignKey(
                        name: "fk_bed_plantings_plant_types_plant_type_id",
                        column: x => x.plant_type_id,
                        principalTable: "plant_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bed_plantings_plants_plant_id",
                        column: x => x.plant_id,
                        principalTable: "plants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_bed_plantings_raised_beds_raised_bed_id",
                        column: x => x.raised_bed_id,
                        principalTable: "raised_beds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pot_use_plants",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    pot_use_id = table.Column<int>(type: "integer", nullable: false),
                    plant_type_id = table.Column<int>(type: "integer", nullable: false),
                    plant_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pot_use_plants", x => x.id);
                    table.ForeignKey(
                        name: "fk_pot_use_plants_plant_types_plant_type_id",
                        column: x => x.plant_type_id,
                        principalTable: "plant_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pot_use_plants_plants_plant_id",
                        column: x => x.plant_id,
                        principalTable: "plants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_pot_use_plants_pot_uses_pot_use_id",
                        column: x => x.pot_use_id,
                        principalTable: "pot_uses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_raised_beds_measures",
                table: "raised_beds",
                sql: "width_cm > 0 AND length_cm > 0 AND (height_cm IS NULL OR height_cm > 0)");

            migrationBuilder.CreateIndex(
                name: "ix_bed_plantings_plant_id",
                table: "bed_plantings",
                column: "plant_id");

            migrationBuilder.CreateIndex(
                name: "ix_bed_plantings_plant_type_id",
                table: "bed_plantings",
                column: "plant_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_bed_plantings_raised_bed_id_plant_type_id_plant_id",
                table: "bed_plantings",
                columns: new[] { "raised_bed_id", "plant_type_id", "plant_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pot_use_plants_plant_id",
                table: "pot_use_plants",
                column: "plant_id");

            migrationBuilder.CreateIndex(
                name: "ix_pot_use_plants_plant_type_id",
                table: "pot_use_plants",
                column: "plant_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_pot_use_plants_pot_use_id_plant_type_id_plant_id",
                table: "pot_use_plants",
                columns: new[] { "pot_use_id", "plant_type_id", "plant_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bed_plantings");

            migrationBuilder.DropTable(
                name: "pot_use_plants");

            migrationBuilder.DropCheckConstraint(
                name: "ck_raised_beds_measures",
                table: "raised_beds");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "raised_beds");

            migrationBuilder.AlterColumn<decimal>(
                name: "height_cm",
                table: "raised_beds",
                type: "numeric(6,1)",
                precision: 6,
                scale: 1,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(6,1)",
                oldPrecision: 6,
                oldScale: 1,
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "plant_type_id",
                table: "pot_uses",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "ck_raised_beds_measures",
                table: "raised_beds",
                sql: "width_cm > 0 AND length_cm > 0 AND height_cm > 0");

            migrationBuilder.CreateIndex(
                name: "ix_pot_uses_plant_type_id",
                table: "pot_uses",
                column: "plant_type_id");

            migrationBuilder.AddForeignKey(
                name: "fk_pot_uses_plant_types_plant_type_id",
                table: "pot_uses",
                column: "plant_type_id",
                principalTable: "plant_types",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
