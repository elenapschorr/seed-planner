using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SeedPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlantRoleAndPartners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "role",
                table: "plants",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "user_plant_partners",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    owner_id = table.Column<string>(type: "text", nullable: false),
                    main_plant_type_id = table.Column<int>(type: "integer", nullable: false),
                    secondary_plant_type_id = table.Column<int>(type: "integer", nullable: false),
                    included = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_plant_partners", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_plant_partners_asp_net_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_plant_partners_plant_types_main_plant_type_id",
                        column: x => x.main_plant_type_id,
                        principalTable: "plant_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_plant_partners_plant_types_secondary_plant_type_id",
                        column: x => x.secondary_plant_type_id,
                        principalTable: "plant_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_plant_partners_main_plant_type_id",
                table: "user_plant_partners",
                column: "main_plant_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_plant_partners_owner_id_main_plant_type_id_secondary_p",
                table: "user_plant_partners",
                columns: new[] { "owner_id", "main_plant_type_id", "secondary_plant_type_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_plant_partners_secondary_plant_type_id",
                table: "user_plant_partners",
                column: "secondary_plant_type_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_plant_partners");

            migrationBuilder.DropColumn(
                name: "role",
                table: "plants");
        }
    }
}
