using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SeedPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlansAndUserDemand : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "plans",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    owner_id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    persons = table.Column<int>(type: "integer", nullable: false),
                    level = table.Column<string>(type: "text", nullable: false),
                    custom_percent = table.Column<int>(type: "integer", nullable: false),
                    include_out_of_stock = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plans", x => x.id);
                    table.CheckConstraint("ck_plans_custom_percent", "custom_percent BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_plans_persons", "persons >= 1");
                    table.ForeignKey(
                        name: "fk_plans_asp_net_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_demands",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    owner_id = table.Column<string>(type: "text", nullable: false),
                    plant_type_id = table.Column<int>(type: "integer", nullable: false),
                    min = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    max = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    unit = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_demands", x => x.id);
                    table.CheckConstraint("ck_user_demands_range", "(\"min\" IS NULL OR \"min\" >= 0) AND (\"max\" IS NULL OR \"max\" >= 0)");
                    table.ForeignKey(
                        name: "fk_user_demands_asp_net_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_demands_plant_types_plant_type_id",
                        column: x => x.plant_type_id,
                        principalTable: "plant_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "plan_items",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    plan_id = table.Column<int>(type: "integer", nullable: false),
                    plant_type_id = table.Column<int>(type: "integer", nullable: false),
                    demand_amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    unit = table.Column<string>(type: "text", nullable: true),
                    calculated_plants = table.Column<int>(type: "integer", nullable: true),
                    plants_override = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_plan_items_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_plan_items_plant_types_plant_type_id",
                        column: x => x.plant_type_id,
                        principalTable: "plant_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "plan_lines",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    plan_item_id = table.Column<int>(type: "integer", nullable: false),
                    plant_id = table.Column<int>(type: "integer", nullable: false),
                    include = table.Column<bool>(type: "boolean", nullable: false),
                    out_of_stock = table.Column<bool>(type: "boolean", nullable: false),
                    calculated_plants = table.Column<int>(type: "integer", nullable: false),
                    plants_override = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_plan_lines_plan_items_plan_item_id",
                        column: x => x.plan_item_id,
                        principalTable: "plan_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_plan_lines_plants_plant_id",
                        column: x => x.plant_id,
                        principalTable: "plants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_plan_items_plan_id_plant_type_id",
                table: "plan_items",
                columns: new[] { "plan_id", "plant_type_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plan_items_plant_type_id",
                table: "plan_items",
                column: "plant_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_plan_lines_plan_item_id_plant_id",
                table: "plan_lines",
                columns: new[] { "plan_item_id", "plant_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plan_lines_plant_id",
                table: "plan_lines",
                column: "plant_id");

            migrationBuilder.CreateIndex(
                name: "ix_plans_owner_id_year_name",
                table: "plans",
                columns: new[] { "owner_id", "year", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_demands_owner_id_plant_type_id",
                table: "user_demands",
                columns: new[] { "owner_id", "plant_type_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_demands_plant_type_id",
                table: "user_demands",
                column: "plant_type_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "plan_lines");

            migrationBuilder.DropTable(
                name: "user_demands");

            migrationBuilder.DropTable(
                name: "plan_items");

            migrationBuilder.DropTable(
                name: "plans");
        }
    }
}
