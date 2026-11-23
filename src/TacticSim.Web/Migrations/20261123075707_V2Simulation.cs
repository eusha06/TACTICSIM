using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TacticSim.Web.Migrations
{
    /// <inheritdoc />
    public partial class V2Simulation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "matches_v2",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scenario = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    difficulty = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    rules_version = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ai_seed = table.Column<int>(type: "integer", nullable: false),
                    turn = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    vp_blue = table.Column<int>(type: "integer", nullable: false),
                    vp_red = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_matches_v2", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "turn_orders_v2",
                columns: table => new
                {
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    turn = table.Column<int>(type: "integer", nullable: false),
                    side = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    command_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    orders = table.Column<string>(type: "jsonb", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_turn_orders_v2", x => new { x.match_id, x.turn, x.side });
                    table.ForeignKey(
                        name: "FK_turn_orders_v2_matches_v2_match_id",
                        column: x => x.match_id,
                        principalTable: "matches_v2",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "turn_results_v2",
                columns: table => new
                {
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    turn = table.Column<int>(type: "integer", nullable: false),
                    replay = table.Column<string>(type: "jsonb", nullable: false),
                    end_snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_turn_results_v2", x => new { x.match_id, x.turn });
                    table.ForeignKey(
                        name: "FK_turn_results_v2_matches_v2_match_id",
                        column: x => x.match_id,
                        principalTable: "matches_v2",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_matches_v2_owner_id_created_at",
                table: "matches_v2",
                columns: new[] { "owner_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_turn_orders_v2_owner_id_command_id",
                table: "turn_orders_v2",
                columns: new[] { "owner_id", "command_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "turn_orders_v2");

            migrationBuilder.DropTable(
                name: "turn_results_v2");

            migrationBuilder.DropTable(
                name: "matches_v2");
        }
    }
}
