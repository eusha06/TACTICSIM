using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TacticSim.Core;
using TacticSim.Web.Services;

namespace TacticSim.Persistence.Tests;

public class MissionReportTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public void Capture_report_counts_completed_rounds_and_actual_survivors_after_restoration()
    {
        var original = Game.CreateRivergate().ExportSnapshot();
        var units = original.Units.Select(u => u with { Health = u.Id == 3 ? u.Health : 0,
            Position = u.Id == 3 ? new Position(5, 3) : u.Position }).ToArray();
        var game = Game.Restore(original with { Units = units });
        for (var round = 0; round < 2; round++) { game.EndTurn(Side.Human); new ComputerOpponent().PlayTurn(game); }
        var report = MissionReport.From(Game.Restore(game.ExportSnapshot()));
        Assert.Equal("Mission success", report.Outcome); Assert.Equal(2, report.CompletedRounds);
        Assert.Equal("2 completed rounds.", report.RoundDescription);
        Assert.Equal((1, 3, 0, 3), (report.FriendlySurvivors, report.FriendlyTotal, report.OpposingSurvivors, report.OpposingTotal));
        Assert.Equal("Secured · 2/2 rounds", report.CaptureResult);
    }

    [Fact]
    public void Defeat_during_computer_turn_does_not_count_an_incomplete_round()
    {
        var snapshot = Game.CreateRivergate().ExportSnapshot();
        var units = snapshot.Units.Select(u => u with { Health = u.Id == 1 ? 1 : u.Id == 5 ? u.Health : 0,
            Position = u.Id == 5 ? new Position(0, 1) : u.Position }).ToArray();
        var game = Game.Restore(snapshot with { Units = units }); game.EndTurn(Side.Human); game.Attack(Side.Computer, 5, 1);
        var report = MissionReport.From(game);
        Assert.Equal("Mission failed", report.Outcome); Assert.Equal(0, report.CompletedRounds);
        Assert.Equal("0 completed rounds. Ended during round 1.", report.RoundDescription);
        Assert.Equal(0, report.FriendlySurvivors); Assert.Equal("Not secured · 0/2 rounds", report.CaptureResult);
    }

    [Fact]
    public void Legacy_report_has_no_capture_claim_and_queries_do_not_mutate_the_game()
    {
        var game = new Game(BattleMap.Default, [new Armor(1, Side.Human, new(0, 0)), new Infantry(2, Side.Computer, new(1, 0))]);
        game.Attack(Side.Human, 1, 2); game.EndTurn(Side.Human); game.EndTurn(Side.Computer); game.Attack(Side.Human, 1, 2);
        var before = JsonSerializer.Serialize(game.ExportSnapshot()); var report = MissionReport.From(game);
        Assert.Equal("Victory", report.Outcome); Assert.Equal(1, report.CompletedRounds);
        Assert.Equal((1, 1), (report.FriendlySurvivors, report.FriendlyTotal));
        Assert.Contains("legacy elimination", report.CaptureResult);
        Assert.Equal(before, JsonSerializer.Serialize(game.ExportSnapshot()));
    }

    [Fact]
    public async Task History_and_resumed_reports_read_real_saved_units_and_older_receipts_allow_missing_reports()
    {
        var owner = Guid.NewGuid();
        await using var db = fixture.Open(); var service = new MatchService(db);
        var command = new MatchCommand(Guid.NewGuid(), 0, "new");
        var started = await service.Execute(owner, command, CancellationToken.None);
        Assert.Equal(3, started.State!.Report!.FriendlySurvivors);
        var saved = await db.Units.SingleAsync(u => u.MatchId == started.State.MatchId && u.UnitId == 1); saved.Health = 0;
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var read = await service.Read(owner, CancellationToken.None);
        var summary = Assert.Single(await service.History(owner, CancellationToken.None));
        Assert.Equal(2, read!.Report!.FriendlySurvivors); Assert.Equal(2, summary.FriendlySurvivors); Assert.Equal(3, summary.FriendlyTotal);
        var receipt = await db.Receipts.SingleAsync(r => r.CommandId == command.CommandId);
        var old = System.Text.Json.Nodes.JsonNode.Parse(receipt.ResponseJson)!.AsObject(); old.Remove("report"); receipt.ResponseJson = old.ToJsonString();
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.Null((await service.Execute(owner, command, CancellationToken.None)).State!.Report);
    }
}
