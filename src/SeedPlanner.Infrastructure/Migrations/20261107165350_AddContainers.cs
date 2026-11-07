using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SeedPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddContainers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "raised_beds",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    owner_id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    shape = table.Column<string>(type: "text", nullable: false),
                    width_cm = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: false),
                    length_cm = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: false),
                    height_cm = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: false),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_raised_beds", x => x.id);
                    table.CheckConstraint("ck_raised_beds_measures", "width_cm > 0 AND length_cm > 0 AND height_cm > 0");
                    table.ForeignKey(
                        name: "fk_raised_beds_asp_net_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "saucers",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    owner_id = table.Column<string>(type: "text", nullable: false),
                    diameter_cm = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: false),
                    color = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saucers", x => x.id);
                    table.CheckConstraint("ck_saucers_diameter", "diameter_cm > 0");
                    table.CheckConstraint("ck_saucers_quantity", "quantity >= 0");
                    table.ForeignKey(
                        name: "fk_saucers_asp_net_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pots",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    owner_id = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    shape = table.Column<string>(type: "text", nullable: false),
                    top_cm = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: true),
                    bottom_cm = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: true),
                    height_cm = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    set_size = table.Column<int>(type: "integer", nullable: false),
                    color = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    saucer_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pots", x => x.id);
                    table.CheckConstraint("ck_pots_measures", "(top_cm IS NULL OR top_cm > 0) AND (bottom_cm IS NULL OR bottom_cm > 0) AND (height_cm IS NULL OR height_cm > 0)");
                    table.CheckConstraint("ck_pots_quantity", "quantity >= 0");
                    table.CheckConstraint("ck_pots_set_size", "set_size >= 1");
                    table.ForeignKey(
                        name: "fk_pots_asp_net_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_pots_saucers_saucer_id",
                        column: x => x.saucer_id,
                        principalTable: "saucers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "pot_uses",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    pot_id = table.Column<int>(type: "integer", nullable: false),
                    plant_type_id = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pot_uses", x => x.id);
                    table.CheckConstraint("ck_pot_uses_quantity", "quantity >= 1");
                    table.ForeignKey(
                        name: "fk_pot_uses_plant_types_plant_type_id",
                        column: x => x.plant_type_id,
                        principalTable: "plant_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pot_uses_pots_pot_id",
                        column: x => x.pot_id,
                        principalTable: "pots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pot_uses_plant_type_id",
                table: "pot_uses",
                column: "plant_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_pot_uses_pot_id",
                table: "pot_uses",
                column: "pot_id");

            migrationBuilder.CreateIndex(
                name: "ix_pots_owner_id",
                table: "pots",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_pots_saucer_id",
                table: "pots",
                column: "saucer_id");

            migrationBuilder.CreateIndex(
                name: "ix_raised_beds_owner_id_name",
                table: "raised_beds",
                columns: new[] { "owner_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_saucers_owner_id",
                table: "saucers",
                column: "owner_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pot_uses");

            migrationBuilder.DropTable(
                name: "raised_beds");

            migrationBuilder.DropTable(
                name: "pots");

            migrationBuilder.DropTable(
                name: "saucers");
        }
    }
}
