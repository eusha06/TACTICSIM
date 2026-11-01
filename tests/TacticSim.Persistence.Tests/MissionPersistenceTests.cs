using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using TacticSim.Core;
using TacticSim.Web.Services;

namespace TacticSim.Persistence.Tests;

public class MissionPersistenceTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static MatchCommand Command(MatchView? s, string kind, Guid? target = null) =>
        new(Guid.NewGuid(), s?.Version ?? 0, kind, MatchId:s?.MatchId,
            ExpectedMatchVersion:s?.MatchVersion, TargetMatchId:target);
    private async Task<ServiceReply> Send(Guid owner, MatchCommand command)
    { await using var db=fixture.Open(); return await new MatchService(db).Execute(owner,command,CancellationToken.None); }
    private async Task<MatchView> Read(Guid owner)
    { await using var db=fixture.Open(); return (await new MatchService(db).Read(owner,CancellationToken.None))!; }
    private async Task<MatchView> Holding(Guid owner)
    {
        var s=(await Send(owner,Command(null,"new"))).State!;
        await using var db=fixture.Open();
        var armor=await db.Units.SingleAsync(u=>u.MatchId==s.MatchId && u.UnitId==2);
        armor.X=5; armor.Y=3;
        foreach(var enemy in await db.Units.Where(u=>u.MatchId==s.MatchId && u.Side==Side.Computer).ToListAsync())enemy.Health=0;
        await db.SaveChangesAsync(); return await Read(owner);
    }

    [Fact]
    public async Task Duplicate_and_concurrent_round_commands_count_once_and_persist_terminal_capture()
    {
        var owner=Guid.NewGuid(); var before=await Holding(owner);
        var first=Command(before,"endTurn"); var s=(await Send(owner,first)).State!;
        Assert.Equal(1,s.Mission!.HeldRounds); Assert.Equal(2,s.Mission.LastEvaluatedComputerTurn);
        Assert.Equal(JsonSerializer.Serialize(s),JsonSerializer.Serialize((await Send(owner,first)).State));
        Assert.Equal(409,(await Send(owner,Command(before,"endTurn"))).Status);
        var second=Command(s,"endTurn"); var results=await Task.WhenAll(Send(owner,second),Send(owner,second));
        Assert.All(results,r=>Assert.Equal(200,r.Status));
        var final=await Read(owner); Assert.Equal("Human",final.Winner); Assert.Equal(2,final.Mission!.HeldRounds);
        Assert.Equal(4,final.Mission.LastEvaluatedComputerTurn);
        Assert.DoesNotContain("still required",final.Mission.Instructions);
        Assert.Contains("Outpost secured",final.Mission.Instructions);
        await using var db=fixture.Open(); var row=await db.Matches.SingleAsync(m=>m.Id==final.MatchId);
        Assert.Equal(MissionType.Rivergate,row.Mission); Assert.Equal((5,3),(row.ObjectiveX,row.ObjectiveY));
        Assert.Equal(2,row.HeldRounds); Assert.Equal(4,row.LastEvaluatedComputerTurn); Assert.NotNull(row.CompletedAt);
        Assert.Equal(2,await db.Events.CountAsync(e=>e.MatchId==row.Id && e.Kind=="ObjectiveHeld"));
        Assert.Single(await db.Events.Where(e=>e.MatchId==row.Id && e.Kind=="MissionCompleted").ToListAsync());
        Assert.Equal(new int?[]{1,2},await db.Events.Where(e=>e.MatchId==row.Id && e.Kind=="ObjectiveHeld").OrderBy(e=>e.Sequence).Select(e=>e.ObjectiveProgress).ToArrayAsync());
        Assert.Equal(3,await db.Receipts.CountAsync(r=>r.MatchId==row.Id));
    }

    [Fact]
    public async Task New_match_and_resume_preserve_partial_progress_evaluation_state_and_action_flags()
    {
        var owner=Guid.NewGuid(); var first=await Holding(owner);
        first=(await Send(owner,Command(first,"endTurn"))).State!;
        first=(await Send(owner,Command(first,"move") with {UnitId=1,X=0,Y=1})).State!;
        var second=(await Send(owner,Command(first,"new"))).State!;
        Assert.Equal(0,second.Mission!.HeldRounds);
        var resumed=(await Send(owner,Command(second,"resume",first.MatchId))).State!;
        Assert.Equal(1,resumed.Mission!.HeldRounds); Assert.Equal(2,resumed.Mission.LastEvaluatedComputerTurn);
        Assert.True(resumed.Units.Single(u=>u.Id==1).HasMoved);
        Assert.Equal(JsonSerializer.Serialize(resumed),JsonSerializer.Serialize(await Read(owner)));
        var final=(await Send(owner,Command(resumed,"endTurn"))).State!;
        Assert.Equal("Human",final.Winner); Assert.Equal(2,final.Mission!.HeldRounds);
    }

    [Fact]
    public async Task Failed_objective_event_rolls_back_progress_round_marker_and_command_atomically()
    {
        var owner=Guid.NewGuid(); var before=await Holding(owner);
        await fixture.Sql($$"""
            CREATE FUNCTION "RejectHold"() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
              IF NEW."MatchId"='{{before.MatchId}}'::uuid AND NEW."Kind"='ObjectiveHeld' THEN
                RAISE EXCEPTION 'Test hold save failure';
              END IF;
              RETURN NEW;
            END $body$;
            CREATE TRIGGER "RejectHold" BEFORE INSERT ON "MatchEvents" FOR EACH ROW EXECUTE FUNCTION "RejectHold"();
            """);
        try { await Assert.ThrowsAsync<DbUpdateException>(()=>Send(owner,Command(before,"endTurn"))); }
        finally { await fixture.Sql("DROP TRIGGER \"RejectHold\" ON \"MatchEvents\"; DROP FUNCTION \"RejectHold\"();"); }
        Assert.Equal(JsonSerializer.Serialize(before),JsonSerializer.Serialize(await Read(owner)));
        await using var db=fixture.Open(); Assert.Single(await db.Receipts.Where(r=>r.MatchId==before.MatchId).ToListAsync());
    }

    [Fact]
    public async Task Legacy_elimination_and_old_cached_receipts_stay_compatible()
    {
        var owner=Guid.NewGuid(); var original=Command(null,"new"); var s=(await Send(owner,original)).State!;
        await using(var db=fixture.Open())
        {
            var match=await db.Matches.SingleAsync(m=>m.Id==s.MatchId);
            match.Mission=MissionType.Elimination; match.ObjectiveX=null; match.ObjectiveY=null;
            var receipt=await db.Receipts.SingleAsync(r=>r.CommandId==original.CommandId);
            var old=JsonNode.Parse(receipt.ResponseJson)!.AsObject(); old.Remove("mission"); receipt.ResponseJson=old.ToJsonString();
            foreach(var enemy in await db.Units.Where(u=>u.MatchId==s.MatchId && u.Side==Side.Computer).ToListAsync())
            { enemy.Health=enemy.UnitId==4?1:0; if(enemy.UnitId==4){enemy.X=0;enemy.Y=1;} }
            await db.SaveChangesAsync();
        }
        var replay=await Send(owner,original); Assert.Equal(200,replay.Status); Assert.Null(replay.State!.Mission);
        s=await Read(owner); Assert.Equal("Elimination",s.Mission!.Type); Assert.Null(s.Mission.X);
        var finished=(await Send(owner,Command(s,"attack") with {UnitId=1,TargetId=4})).State!;
        Assert.Equal("Human",finished.Winner); Assert.Equal(0,finished.Mission!.HeldRounds);
        await using var check=fixture.Open(); Assert.Single(await check.Matches.Where(m=>m.OwnerId==owner).ToListAsync());
    }
}
