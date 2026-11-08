using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SeedPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPotGroupsAndPlantings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_pots_asp_net_users_owner_id",
                table: "pots");

            migrationBuilder.DropForeignKey(
                name: "fk_pots_saucers_saucer_id",
                table: "pots");

            migrationBuilder.DropTable(
                name: "bed_plantings");

            migrationBuilder.DropTable(
                name: "pot_use_plants");

            migrationBuilder.DropTable(
                name: "pot_uses");

            migrationBuilder.DropIndex(
                name: "ix_pots_owner_id",
                table: "pots");

            migrationBuilder.DropIndex(
                name: "ix_pots_saucer_id",
                table: "pots");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pots_measures",
                table: "pots");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pots_quantity",
                table: "pots");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pots_set_size",
                table: "pots");

            migrationBuilder.DropColumn(
                name: "bottom_cm",
                table: "pots");

            migrationBuilder.DropColumn(
                name: "code",
                table: "pots");

            migrationBuilder.DropColumn(
                name: "color",
                table: "pots");

            migrationBuilder.DropColumn(
                name: "height_cm",
                table: "pots");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "pots");

            migrationBuilder.DropColumn(
                name: "owner_id",
                table: "pots");

            migrationBuilder.DropColumn(
                name: "quantity",
                table: "pots");

            migrationBuilder.DropColumn(
                name: "saucer_id",
                table: "pots");

            migrationBuilder.DropColumn(
                name: "shape",
                table: "pots");

            migrationBuilder.DropColumn(
                name: "top_cm",
                table: "pots");

            migrationBuilder.RenameColumn(
                name: "set_size",
                table: "pots",
                newName: "pot_group_id");

            migrationBuilder.CreateTable(
                name: "plantings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    owner_id = table.Column<string>(type: "text", nullable: false),
                    plant_type_id = table.Column<int>(type: "integer", nullable: false),
                    plant_id = table.Column<int>(type: "integer", nullable: true),
                    plant_count = table.Column<int>(type: "integer", nullable: true),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    pot_id = table.Column<int>(type: "integer", nullable: true),
                    raised_bed_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plantings", x => x.id);
                    table.CheckConstraint("ck_plantings_count", "plant_count IS NULL OR plant_count >= 1");
                    table.CheckConstraint("ck_plantings_one_place", "num_nonnulls(pot_id, raised_bed_id) <= 1");
                    table.ForeignKey(
                        name: "fk_plantings_asp_net_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_plantings_plant_types_plant_type_id",
                        column: x => x.plant_type_id,
                        principalTable: "plant_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_plantings_plants_plant_id",
                        column: x => x.plant_id,
                        principalTable: "plants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_plantings_pots_pot_id",
                        column: x => x.pot_id,
                        principalTable: "pots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_plantings_raised_beds_raised_bed_id",
                        column: x => x.raised_bed_id,
                        principalTable: "raised_beds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pot_groups",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    owner_id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    shape = table.Column<string>(type: "text", nullable: false),
                    top_cm = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: true),
                    bottom_cm = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: true),
                    height_cm = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: true),
                    set_size = table.Column<int>(type: "integer", nullable: false),
                    color = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    saucer_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pot_groups", x => x.id);
                    table.CheckConstraint("ck_pot_groups_measures", "(top_cm IS NULL OR top_cm > 0) AND (bottom_cm IS NULL OR bottom_cm > 0) AND (height_cm IS NULL OR height_cm > 0)");
                    table.CheckConstraint("ck_pot_groups_set_size", "set_size >= 1");
                    table.ForeignKey(
                        name: "fk_pot_groups_asp_net_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_pot_groups_saucers_saucer_id",
                        column: x => x.saucer_id,
                        principalTable: "saucers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pots_pot_group_id",
                table: "pots",
                column: "pot_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_plantings_owner_id",
                table: "plantings",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_plantings_plant_id",
                table: "plantings",
                column: "plant_id");

            migrationBuilder.CreateIndex(
                name: "ix_plantings_plant_type_id",
                table: "plantings",
                column: "plant_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_plantings_pot_id",
                table: "plantings",
                column: "pot_id");

            migrationBuilder.CreateIndex(
                name: "ix_plantings_raised_bed_id",
                table: "plantings",
                column: "raised_bed_id");

            migrationBuilder.CreateIndex(
                name: "ix_pot_groups_owner_id_name",
                table: "pot_groups",
                columns: new[] { "owner_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pot_groups_saucer_id",
                table: "pot_groups",
                column: "saucer_id");

            migrationBuilder.AddForeignKey(
                name: "fk_pots_pot_groups_pot_group_id",
                table: "pots",
                column: "pot_group_id",
                principalTable: "pot_groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_pots_pot_groups_pot_group_id",
                table: "pots");

            migrationBuilder.DropTable(
                name: "plantings");

            migrationBuilder.DropTable(
                name: "pot_groups");

            migrationBuilder.DropIndex(
                name: "ix_pots_pot_group_id",
                table: "pots");

            migrationBuilder.RenameColumn(
                name: "pot_group_id",
                table: "pots",
                newName: "set_size");

            migrationBuilder.AddColumn<decimal>(
                name: "bottom_cm",
                table: "pots",
                type: "numeric(6,1)",
                precision: 6,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "code",
                table: "pots",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "color",
                table: "pots",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "height_cm",
                table: "pots",
                type: "numeric(6,1)",
                precision: 6,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "pots",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "owner_id",
                table: "pots",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "quantity",
                table: "pots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "saucer_id",
                table: "pots",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shape",
                table: "pots",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "top_cm",
                table: "pots",
                type: "numeric(6,1)",
                precision: 6,
                scale: 1,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "bed_plantings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    plant_id = table.Column<int>(type: "integer", nullable: true),
                    plant_type_id = table.Column<int>(type: "integer", nullable: false),
                    raised_bed_id = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    plant_count = table.Column<int>(type: "integer", nullable: true)
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
                name: "pot_uses",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    pot_id = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pot_uses", x => x.id);
                    table.CheckConstraint("ck_pot_uses_quantity", "quantity >= 1");
                    table.ForeignKey(
                        name: "fk_pot_uses_pots_pot_id",
                        column: x => x.pot_id,
                        principalTable: "pots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pot_use_plants",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    plant_id = table.Column<int>(type: "integer", nullable: true),
                    plant_type_id = table.Column<int>(type: "integer", nullable: false),
                    pot_use_id = table.Column<int>(type: "integer", nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "ix_pots_owner_id",
                table: "pots",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_pots_saucer_id",
                table: "pots",
                column: "saucer_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pots_measures",
                table: "pots",
                sql: "(top_cm IS NULL OR top_cm > 0) AND (bottom_cm IS NULL OR bottom_cm > 0) AND (height_cm IS NULL OR height_cm > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pots_quantity",
                table: "pots",
                sql: "quantity >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pots_set_size",
                table: "pots",
                sql: "set_size >= 1");

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

            migrationBuilder.CreateIndex(
                name: "ix_pot_uses_pot_id",
                table: "pot_uses",
                column: "pot_id");

            migrationBuilder.AddForeignKey(
                name: "fk_pots_asp_net_users_owner_id",
                table: "pots",
                column: "owner_id",
                principalTable: "asp_net_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_pots_saucers_saucer_id",
                table: "pots",
                column: "saucer_id",
                principalTable: "saucers",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
