using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TacticSim.Core;
using TacticSim.Web.Services;

namespace TacticSim.Persistence.Tests;

public class LegacyMigrationTests
{
    [Fact]
    public async Task Migration_preserves_current_and_older_snapshots_as_relational_rows()
    {
        var owner = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var currentId = Guid.NewGuid();
        var firstGame = Game.CreateDefault();
        Assert.True(firstGame.Move(Side.Human, 1, new Position(0, 1)).IsSuccess);
        var first = LegacyView(firstGame, firstId, 2, true);
        var current = LegacyView(Game.CreateDefault(), currentId, 3, false);
        using var database = new PostgresFixture(connection =>
        {
            using var seed = new NpgsqlCommand("""
                CREATE TABLE "Matches" ("OwnerId" uuid PRIMARY KEY, "MatchId" uuid, "Version" bigint,
                    "StateJson" text, "HistoryJson" text);
                CREATE TABLE "Receipts" ("OwnerId" uuid, "CommandId" uuid, "Fingerprint" text, "ResponseJson" text);
                CREATE TABLE "Results" ("OwnerId" uuid, "MatchId" uuid, "Winner" text, "Turns" integer, "CompletedAt" timestamptz);
                INSERT INTO "Matches" VALUES (@owner, @current, 3, '{}', '[]');
                INSERT INTO "Receipts" VALUES (@owner, @command1, 'legacy-one', @first);
                INSERT INTO "Receipts" VALUES (@owner, @command2, 'legacy-two', @second);
                """, connection);
            seed.Parameters.AddWithValue("owner", owner);
            seed.Parameters.AddWithValue("current", currentId);
            seed.Parameters.AddWithValue("command1", Guid.NewGuid());
            seed.Parameters.AddWithValue("command2", Guid.NewGuid());
            seed.Parameters.AddWithValue("first", first);
            seed.Parameters.AddWithValue("second", current);
            seed.ExecuteNonQuery();
        });
        await using var db = database.Open();
        Assert.Equal(2, await db.Matches.CountAsync());
        Assert.Equal(12, await db.Units.CountAsync());
        Assert.Equal(3, await db.Events.CountAsync());
        Assert.Equal(2, await db.Receipts.CountAsync());
        Assert.All(await db.Events.ToListAsync(), entry => Assert.Equal("Legacy", entry.Kind));
        var service = new MatchService(db);
        var currentState = (await service.Read(owner, CancellationToken.None))!;
        Assert.Equal(currentId, currentState.MatchId);
        var archived = (await service.ReadMatch(owner, firstId, CancellationToken.None))!;
        Assert.Equal(new long[] { 1, 2 }, archived.History.Select(e => e.Sequence));
        Assert.True(archived.Units.Single(u => u.Id == 1).HasMoved);
        Assert.Equal(1, archived.Units.Single(u => u.Id == 1).Y);
        // Applying migrations again is a no-op, not another import or reseed.
        await db.Database.MigrateAsync();
        Assert.Equal(3, await db.Events.CountAsync());
        var resumed = await service.Execute(owner, new MatchCommand(Guid.NewGuid(), currentState.Version, "resume",
            MatchId: currentId, ExpectedMatchVersion: currentState.MatchVersion, TargetMatchId: firstId), CancellationToken.None);
        Assert.Equal(200, resumed.Status);
        Assert.True(resumed.State!.Units.Single(u => u.Id == 1).HasMoved);
        Assert.Empty(resumed.State.Units.Single(u => u.Id == 1).LegalMoves);
    }

    private static string LegacyView(Game game, Guid matchId, long version, bool moved)
    {
        var history = new List<object> { new { turn = 1, side = "Human", text = "Match started. Your blue squad moves first." } };
        if (moved) history.Add(new { turn = 1, side = "Human", text = "You: Infantry #1 moved to A2." });
        return JsonSerializer.Serialize(new
        {
            matchId, version, currentSide = game.CurrentSide.ToString(), turnNumber = game.TurnNumber,
            winner = (string?)null,
            units = game.Units.Select(u => new { id = u.Id, type = u.GetType().Name, side = u.Side.ToString(),
                x = u.Position.X, y = u.Position.Y, health = u.Health, hasMoved = u.HasMoved, hasAttacked = u.HasAttacked }),
            history
        });
    }
}
