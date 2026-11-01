using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TacticSim.Web.Services;

namespace TacticSim.Persistence.Tests;

public sealed class PersistenceTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static MatchCommand Command(MatchView? state, string kind, int? unit = null,
        int? target = null, int? x = null, int? y = null, Guid? resume = null) =>
        new(Guid.NewGuid(), state?.Version ?? 0, kind, unit, target, x, y,
            state?.MatchId, state?.MatchVersion, resume);

    private async Task<ServiceReply> Send(Guid owner, MatchCommand command)
    {
        await using var db = fixture.Open();
        return await new MatchService(db).Execute(owner, command, CancellationToken.None);
    }

    private async Task<MatchView> Start(Guid owner)
    {
        var reply = await Send(owner, Command(null, "new"));
        Assert.Equal(200, reply.Status);
        return Assert.IsType<MatchView>(reply.State);
    }

    private async Task<MatchView?> Read(Guid owner)
    {
        await using var db = fixture.Open();
        return await new MatchService(db).Read(owner, CancellationToken.None);
    }

    [Fact]
    public async Task Fresh_context_restores_relational_health_positions_flags_and_ordered_events()
    {
        var owner = Guid.NewGuid();
        var state = await Start(owner);
        await using (var db = fixture.Open())
        {
            // Arrange a nearby enemy in this test-owned match; combat still uses Core.
            var target = await db.Units.SingleAsync(u => u.MatchId == state.MatchId && u.UnitId == 5);
            target.X = 1;
            target.Y = 1;
            await db.SaveChangesAsync();
        }
        var move = Command(state, "move", 1, x: 0, y: 1);
        state = (await Send(owner, move)).State!;
        var attack = Command(state, "attack", 1, target: 5);
        var reply = await Send(owner, attack);
        Assert.Equal(200, reply.Status);
        state = reply.State!;

        Assert.Equal(JsonSerializer.Serialize(state), JsonSerializer.Serialize(await Read(owner)));
        await using var verify = fixture.Open();
        Assert.Equal(6, await verify.Units.CountAsync(u => u.MatchId == state.MatchId));
        var human = await verify.Units.SingleAsync(u => u.MatchId == state.MatchId && u.UnitId == 1);
        Assert.Equal((0, 1), (human.X, human.Y));
        Assert.True(human.HasMoved);
        Assert.True(human.HasAttacked);
        Assert.Equal(12, (await verify.Units.SingleAsync(u => u.MatchId == state.MatchId && u.UnitId == 5)).Health);
        var events = await verify.Events.Where(e => e.MatchId == state.MatchId).OrderBy(e => e.Sequence).ToListAsync();
        Assert.Equal(new long[] { 1, 2, 3 }, events.Select(e => e.Sequence));
        Assert.Equal(new[] { "MatchStarted", "Move", "Attack" }, events.Select(e => e.Kind));
        Assert.Equal(attack.CommandId, events[2].CommandId);
        Assert.Equal(4, events[2].Damage);
        Assert.Equal(3, await verify.Receipts.CountAsync(r => r.MatchId == state.MatchId));
    }

    [Fact]
    public async Task Concurrent_duplicate_command_applies_once_with_one_event_and_receipt()
    {
        var owner = Guid.NewGuid();
        var before = await Start(owner);
        var command = Command(before, "move", 1, x: 0, y: 1);
        var replies = await Task.WhenAll(Send(owner, command), Send(owner, command));
        Assert.All(replies, reply => Assert.Equal(200, reply.Status));
        Assert.Equal(JsonSerializer.Serialize(replies[0].State), JsonSerializer.Serialize(replies[1].State));
        await using var db = fixture.Open();
        Assert.Equal(2, await db.Events.CountAsync(e => e.MatchId == before.MatchId));
        Assert.Equal(2, await db.Receipts.CountAsync(r => r.MatchId == before.MatchId));
        Assert.Equal(before.MatchVersion + 1, (await Read(owner))!.MatchVersion);
        Assert.Equal(409, (await Send(owner, command with { X = 1 })).Status);
    }

    [Fact]
    public async Task Duplicate_committing_between_receipt_and_version_reads_returns_its_saved_result()
    {
        var owner = Guid.NewGuid();
        var before = await Start(owner);
        var command = Command(before, "move", 1, x: 0, y: 1);
        ServiceReply? winner = null;
        var race = new CommitAfterReceiptRead(async () => winner = await Send(owner, command));
        await using var db = fixture.Open(race);
        var reply = await new MatchService(db).Execute(owner, command, CancellationToken.None);
        Assert.Equal(200, winner!.Status);
        Assert.Equal(200, reply.Status);
        Assert.Equal(JsonSerializer.Serialize(winner.State), JsonSerializer.Serialize(reply.State));
        Assert.Equal(2, await db.Events.CountAsync(e => e.MatchId == before.MatchId));
        Assert.Equal(2, await db.Receipts.CountAsync(r => r.MatchId == before.MatchId));
    }

    // The first SELECT has already returned an empty receipt result from PostgreSQL.
    // Commit the competing request before EF continues to read the session version.
    private sealed class CommitAfterReceiptRead(Func<Task> commitWinner) : DbCommandInterceptor
    {
        private bool _committed;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (!_committed && command.CommandText.Contains("FROM \"MatchCommands\""))
            {
                _committed = true;
                await commitWinner();
            }
            return result;
        }
    }

    [Fact]
    public async Task Concurrent_distinct_commands_at_same_match_version_have_one_winner()
    {
        var owner = Guid.NewGuid();
        var before = await Start(owner);
        var replies = await Task.WhenAll(Send(owner, Command(before, "move", 1, x: 0, y: 1)),
            Send(owner, Command(before, "move", 1, x: 0, y: 2)));
        Assert.Equal(new[] { 200, 409 }, replies.Select(r => r.Status).Order());
        await using var db = fixture.Open();
        Assert.Equal(2, await db.Events.CountAsync(e => e.MatchId == before.MatchId));
        Assert.Equal(2, await db.Receipts.CountAsync(r => r.MatchId == before.MatchId));
    }

    [Fact]
    public async Task Database_failure_rolls_back_match_units_events_receipt_and_session_revision()
    {
        var owner = Guid.NewGuid();
        var before = await Start(owner);
        await fixture.Sql($$"""
            CREATE FUNCTION "RejectTestEvent"() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
                IF NEW."MatchId" = '{{before.MatchId}}'::uuid AND NEW."Kind" = 'Move' THEN
                    RAISE EXCEPTION 'Simulated event insert failure';
                END IF;
                RETURN NEW;
            END $body$;
            CREATE TRIGGER "RejectTestEvent" BEFORE INSERT ON "MatchEvents"
                FOR EACH ROW EXECUTE FUNCTION "RejectTestEvent"();
            """);
        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => Send(owner, Command(before, "move", 1, x: 0, y: 1)));
        }
        finally
        {
            await fixture.Sql("DROP TRIGGER \"RejectTestEvent\" ON \"MatchEvents\"; DROP FUNCTION \"RejectTestEvent\"();");
        }
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(await Read(owner)));
        await using var db = fixture.Open();
        Assert.Single(await db.Events.Where(e => e.MatchId == before.MatchId).ToListAsync());
        Assert.Single(await db.Receipts.Where(r => r.MatchId == before.MatchId).ToListAsync());
    }

    [Fact]
    public async Task Resume_keeps_action_limits_and_new_match_does_not_overwrite_old_matches()
    {
        var owner = Guid.NewGuid();
        var first = await Start(owner);
        first = (await Send(owner, Command(first, "move", 1, x: 0, y: 1))).State!;
        var second = (await Send(owner, Command(first, "new"))).State!;
        Assert.NotEqual(first.MatchId, second.MatchId);
        Assert.Equal(1, second.MatchVersion);
        Assert.True(second.Version > first.Version);
        var resumed = await Send(owner, Command(second, "resume", resume: first.MatchId));
        Assert.Equal(200, resumed.Status);
        var state = resumed.State!;
        Assert.Equal(first.MatchId, state.MatchId);
        Assert.True(state.Units.Single(u => u.Id == 1).HasMoved);
        Assert.Empty(state.Units.Single(u => u.Id == 1).LegalMoves);
        Assert.Equal(422, (await Send(owner, Command(state, "move", 1, x: 0, y: 2))).Status);
        Assert.Equal(409, (await Send(owner, Command(second, "endTurn"))).Status);
        // Even with the current session revision, an inactive match cannot be commanded.
        Assert.Equal(409, (await Send(owner, Command(second, "endTurn") with { ExpectedVersion = state.Version })).Status);
        await using var db = fixture.Open();
        var history = await new MatchService(db).History(owner, CancellationToken.None);
        Assert.Equal(2, history.Count);
        Assert.Single(history, m => m.IsActive && m.MatchId == first.MatchId);
        Assert.Equal("MatchResumed", (await db.Events.SingleAsync(e => e.MatchId == first.MatchId && e.Sequence == 3)).Kind);
    }

    [Fact]
    public async Task Reads_history_resume_and_commands_are_scoped_to_owner()
    {
        var owner = Guid.NewGuid();
        var otherOwner = Guid.NewGuid();
        var own = await Start(owner);
        var other = await Start(otherOwner);
        await using var db = fixture.Open();
        var service = new MatchService(db);
        Assert.Null(await service.ReadMatch(otherOwner, own.MatchId, CancellationToken.None));
        Assert.Equal(other.MatchId, Assert.Single(await service.History(otherOwner, CancellationToken.None)).MatchId);
        Assert.Equal(404, (await Send(otherOwner, Command(other, "resume", resume: own.MatchId))).Status);
        Assert.Equal(409, (await Send(otherOwner, Command(other, "move", 1, x: 0, y: 1) with { MatchId = own.MatchId })).Status);
        Assert.Equal(JsonSerializer.Serialize(own), JsonSerializer.Serialize(await Read(owner)));
        Assert.Equal(JsonSerializer.Serialize(other), JsonSerializer.Serialize(await Read(otherOwner)));
    }

    [Fact]
    public async Task Stale_match_version_and_illegal_commands_do_not_append_events_or_receipts()
    {
        var owner = Guid.NewGuid();
        var state = await Start(owner);
        Assert.Equal(409, (await Send(owner, Command(state, "move", 1, x: 0, y: 1) with { ExpectedMatchVersion = 0 })).Status);
        Assert.Equal(422, (await Send(owner, Command(state, "move", 1, x: 4, y: 2))).Status); // water
        Assert.Equal(JsonSerializer.Serialize(state), JsonSerializer.Serialize(await Read(owner)));
        await using var db = fixture.Open();
        Assert.Single(await db.Events.Where(e => e.MatchId == state.MatchId).ToListAsync());
        Assert.Single(await db.Receipts.Where(r => r.MatchId == state.MatchId).ToListAsync());
    }
}
