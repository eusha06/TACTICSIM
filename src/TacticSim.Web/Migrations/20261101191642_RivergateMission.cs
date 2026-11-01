using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TacticSim.Web.Migrations
{
    /// <inheritdoc />
    public partial class RivergateMission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ObjectiveProgress",
                table: "MatchEvents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HeldRounds",
                table: "GameMatches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LastEvaluatedComputerTurn",
                table: "GameMatches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Mission",
                table: "GameMatches",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "Elimination");

            migrationBuilder.AddColumn<int>(
                name: "ObjectiveX",
                table: "GameMatches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ObjectiveY",
                table: "GameMatches",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Mission data must be retained. Fix forward or restore a verified backup; do not drop mission columns.");
    }
}
