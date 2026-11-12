using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SeedPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanAllocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "plan_id",
                table: "plantings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "allocated_at",
                table: "plans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "allocation_hash",
                table: "plans",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "allocation_json",
                table: "plans",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "allow_others_in_covered_beds",
                table: "plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "max_bed_area_square_meters",
                table: "plans",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "no_cover",
                table: "plans",
                type: "text",
                nullable: false,
                defaultValue: "NewCoveredBed");

            migrationBuilder.AddColumn<string>(
                name: "shortage",
                table: "plans",
                type: "text",
                nullable: false,
                defaultValue: "ShrinkProportionally");

            migrationBuilder.CreateTable(
                name: "plan_containers",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    plan_id = table.Column<int>(type: "integer", nullable: false),
                    pot_id = table.Column<int>(type: "integer", nullable: true),
                    raised_bed_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_containers", x => x.id);
                    table.CheckConstraint("ck_plan_containers_one", "(pot_id IS NOT NULL AND raised_bed_id IS NULL) OR (pot_id IS NULL AND raised_bed_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_plan_containers_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_plan_containers_pots_pot_id",
                        column: x => x.pot_id,
                        principalTable: "pots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_plan_containers_raised_beds_raised_bed_id",
                        column: x => x.raised_bed_id,
                        principalTable: "raised_beds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_plantings_plan_id",
                table: "plantings",
                column: "plan_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_plans_max_bed_area",
                table: "plans",
                sql: "max_bed_area_square_meters >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_plan_containers_plan_id_pot_id",
                table: "plan_containers",
                columns: new[] { "plan_id", "pot_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plan_containers_plan_id_raised_bed_id",
                table: "plan_containers",
                columns: new[] { "plan_id", "raised_bed_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plan_containers_pot_id",
                table: "plan_containers",
                column: "pot_id");

            migrationBuilder.CreateIndex(
                name: "ix_plan_containers_raised_bed_id",
                table: "plan_containers",
                column: "raised_bed_id");

            migrationBuilder.AddForeignKey(
                name: "fk_plantings_plans_plan_id",
                table: "plantings",
                column: "plan_id",
                principalTable: "plans",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_plantings_plans_plan_id",
                table: "plantings");

            migrationBuilder.DropTable(
                name: "plan_containers");

            migrationBuilder.DropIndex(
                name: "ix_plantings_plan_id",
                table: "plantings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_plans_max_bed_area",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "plan_id",
                table: "plantings");

            migrationBuilder.DropColumn(
                name: "allocated_at",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "allocation_hash",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "allocation_json",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "allow_others_in_covered_beds",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "max_bed_area_square_meters",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "no_cover",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "shortage",
                table: "plans");
        }
    }
}
