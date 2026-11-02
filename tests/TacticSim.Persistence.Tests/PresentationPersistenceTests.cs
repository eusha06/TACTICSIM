using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TacticSim.Web.Services;

namespace TacticSim.Persistence.Tests;

public class PresentationPersistenceTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private async Task<ServiceReply> Send(Guid owner, MatchCommand command)
    { await using var db = fixture.Open(); return await new MatchService(db).Execute(owner, command, CancellationToken.None); }
    private static MatchCommand Command(MatchView? s, string kind) => new(Guid.NewGuid(), s?.Version ?? 0,
        kind, MatchId: s?.MatchId, ExpectedMatchVersion: s?.MatchVersion);

    [Fact]
    public async Task Receipt_replays_exact_structured_sequence_and_reads_do_not_replay_it()
    {
        var owner = Guid.NewGuid(); var initial = await Send(owner, Command(null, "new"));
        Assert.Null(initial.Presentation); var before = initial.State!;
        var command = Command(before, "endTurn"); var reply = await Send(owner, command);
        var batch = Assert.IsType<PresentationBatch>(reply.Presentation);
        Assert.Equal(command.CommandId, batch.CommandId); Assert.Equal(before.MatchVersion, batch.FromMatchVersion);
        Assert.Equal(reply.State!.MatchVersion, batch.ToMatchVersion);
        Assert.Equal(Enumerable.Range(2, batch.Steps.Length).Select(i => (long)i), batch.Steps.Select(s => s.Sequence));
        Assert.Contains(batch.Steps, s => s.Kind == "Move" && s.Side == "Computer" && s.Route!.Length > 1);
        Assert.Equal(JsonSerializer.Serialize(reply), JsonSerializer.Serialize(await Send(owner, command)));
        await using var db = fixture.Open();
        Assert.Equal(JsonSerializer.Serialize(reply.State), JsonSerializer.Serialize(await new MatchService(db).Read(owner, CancellationToken.None)));
        Assert.Equal(JsonSerializer.Serialize(reply.State.Units), JsonSerializer.Serialize(batch.Steps.Last().Frame.Units
            .Select(u => u with { LegalMoves = reply.State.Units.Single(v => v.Id == u.Id).LegalMoves,
                LegalTargets = reply.State.Units.Single(v => v.Id == u.Id).LegalTargets })));
        Assert.Equal(batch.Steps.Length, await db.Events.CountAsync(e => e.CommandId == command.CommandId));
        Assert.Equal(409, (await Send(owner, Command(before, "endTurn"))).Status);
        Assert.Null((await Send(Guid.NewGuid(), command)).Presentation);
    }

    [Fact]
    public async Task Concurrent_retries_return_identical_frames_and_only_one_set_of_events()
    {
        var owner = Guid.NewGuid(); var before = (await Send(owner, Command(null, "new"))).State!;
        var command = Command(before, "move") with { UnitId = 2, X = 1, Y = 1 };
        var replies = await Task.WhenAll(Send(owner, command), Send(owner, command));
        Assert.All(replies, reply => Assert.Equal(200, reply.Status));
        Assert.Equal(JsonSerializer.Serialize(replies[0]), JsonSerializer.Serialize(replies[1]));
        var step = Assert.Single(replies[0].Presentation!.Steps);
        Assert.Equal((1, 1), (step.Route![^1].X, step.Route[^1].Y));
        await using var db = fixture.Open(); Assert.Single(await db.Events.Where(e => e.CommandId == command.CommandId).ToListAsync());
        var rejected = await Send(owner, Command(replies[0].State, "move") with { UnitId = 2, X = 4, Y = 0 });
        Assert.Equal(422, rejected.Status); Assert.Null(rejected.Presentation);
    }
}
