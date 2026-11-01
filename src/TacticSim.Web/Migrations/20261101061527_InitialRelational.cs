using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TacticSim.Web.Migrations
{
    /// <inheritdoc />
    public partial class InitialRelational : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GameSessions",
                columns: table => new
                {
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActiveMatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameSessions", x => x.OwnerId);
                });

            migrationBuilder.CreateTable(
                name: "GameMatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    MapKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CurrentSide = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TurnNumber = table.Column<int>(type: "integer", nullable: false),
                    Winner = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastEventSequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameMatches", x => x.Id);
                    table.UniqueConstraint("AK_GameMatches_OwnerId_Id", x => new { x.OwnerId, x.Id });
                    table.ForeignKey(
                        name: "FK_GameMatches_GameSessions_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "GameSessions",
                        principalColumn: "OwnerId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MatchCommands",
                columns: table => new
                {
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommandId = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    UnitId = table.Column<int>(type: "integer", nullable: true),
                    TargetId = table.Column<int>(type: "integer", nullable: true),
                    X = table.Column<int>(type: "integer", nullable: true),
                    Y = table.Column<int>(type: "integer", nullable: true),
                    TargetMatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpectedMatchVersion = table.Column<long>(type: "bigint", nullable: true),
                    ResultingMatchVersion = table.Column<long>(type: "bigint", nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResponseJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchCommands", x => new { x.OwnerId, x.CommandId });
                    table.UniqueConstraint("AK_MatchCommands_MatchId_CommandId", x => new { x.MatchId, x.CommandId });
                    table.ForeignKey(
                        name: "FK_MatchCommands_GameMatches_OwnerId_MatchId",
                        columns: x => new { x.OwnerId, x.MatchId },
                        principalTable: "GameMatches",
                        principalColumns: new[] { "OwnerId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MatchUnits",
                columns: table => new
                {
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Side = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    X = table.Column<int>(type: "integer", nullable: false),
                    Y = table.Column<int>(type: "integer", nullable: false),
                    Health = table.Column<int>(type: "integer", nullable: false),
                    HasMoved = table.Column<bool>(type: "boolean", nullable: false),
                    HasAttacked = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchUnits", x => new { x.MatchId, x.UnitId });
                    table.ForeignKey(
                        name: "FK_MatchUnits_GameMatches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "GameMatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchEvents",
                columns: table => new
                {
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    CommandId = table.Column<Guid>(type: "uuid", nullable: true),
                    Turn = table.Column<int>(type: "integer", nullable: false),
                    Side = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    UnitId = table.Column<int>(type: "integer", nullable: true),
                    TargetId = table.Column<int>(type: "integer", nullable: true),
                    X = table.Column<int>(type: "integer", nullable: true),
                    Y = table.Column<int>(type: "integer", nullable: true),
                    Damage = table.Column<int>(type: "integer", nullable: false),
                    Destroyed = table.Column<bool>(type: "boolean", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchEvents", x => new { x.MatchId, x.Sequence });
                    table.ForeignKey(
                        name: "FK_MatchEvents_GameMatches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "GameMatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchEvents_MatchCommands_MatchId_CommandId",
                        columns: x => new { x.MatchId, x.CommandId },
                        principalTable: "MatchCommands",
                        principalColumns: new[] { "MatchId", "CommandId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameMatches_OwnerId_CreatedAt",
                table: "GameMatches",
                columns: new[] { "OwnerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchCommands_OwnerId_MatchId",
                table: "MatchCommands",
                columns: new[] { "OwnerId", "MatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchEvents_MatchId_CommandId",
                table: "MatchEvents",
                columns: new[] { "MatchId", "CommandId" });

            using var source = typeof(InitialRelational).Assembly.GetManifestResourceStream("TacticSim.LegacyImport.sql")!;
            using var reader = new System.IO.StreamReader(source);
            migrationBuilder.Sql(reader.ReadToEnd());
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MatchEvents");

            migrationBuilder.DropTable(
                name: "MatchUnits");

            migrationBuilder.DropTable(
                name: "MatchCommands");

            migrationBuilder.DropTable(
                name: "GameMatches");

            migrationBuilder.DropTable(
                name: "GameSessions");
        }
    }
}
