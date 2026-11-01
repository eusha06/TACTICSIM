using System.Text.Json;
using TacticSim.Core;

namespace TacticSim.Tests;

public class RivergateTests
{
    private static Game Quiet(string type = "Armor") => new(new BattleMap(Scenarios.PlainTiles()),
        [Scenarios.Unit(type, 1, Side.Human, 0, 0), new Infantry(2, Side.Computer, new Position(7, 7))],
        mission: MissionType.Rivergate, objective: new Position(0, 0));

    [Theory]
    [InlineData("Infantry")]
    [InlineData("Armor")]
    [InlineData("Artillery")]
    public void Any_friendly_type_can_capture_only_after_two_complete_rounds(string type)
    {
        var game = Quiet(type);
        Assert.Equal(0, game.HeldRounds);
        Assert.True(game.EndTurn(Side.Human).IsSuccess);
        Assert.Equal(0, game.HeldRounds);
        Assert.True(game.EndTurn(Side.Computer).IsSuccess);
        Assert.Equal(1, game.HeldRounds);
        Assert.Equal(2, game.LastEvaluatedComputerTurn);
        Assert.Null(game.Winner);
        for (var i = 0; i < 3; i++) { game.GetLegalMoves(Side.Human, 1); game.GetLegalTargets(Side.Human, 1); }
        Assert.Equal(GameError.NotYourTurn, game.EndTurn(Side.Computer).Error);
        Assert.Equal(1, game.HeldRounds);
        Scenarios.NextHumanTurn(game);
        Assert.Equal(2, game.HeldRounds);
        Assert.Equal(4, game.LastEvaluatedComputerTurn);
        Assert.Equal(Side.Human, game.Winner);
        Assert.Single(game.Actions, a => a.Kind == ActionKind.MissionCompleted);
        Assert.Equal(GameError.GameOver, game.EndTurn(Side.Human).Error);
    }

    [Fact]
    public void Moving_off_resets_immediately_even_if_another_unit_reoccupies_that_round()
    {
        var game = new Game(new BattleMap(Scenarios.PlainTiles()),
            [new Infantry(1, Side.Human, new(0,0)), new Armor(3, Side.Human, new(2,0)),
             new Infantry(2, Side.Computer, new(7,7))], mission: MissionType.Rivergate, objective: new(0,0));
        Scenarios.NextHumanTurn(game);
        Assert.Equal(1, game.HeldRounds);
        Assert.True(game.Move(Side.Human, 1, new(0,1)).IsSuccess);
        Assert.Equal(0, game.HeldRounds);
        Assert.True(game.Move(Side.Human, 3, new(0,0)).IsSuccess);
        Assert.Equal(0, game.HeldRounds);
        Scenarios.NextHumanTurn(game);
        Assert.Equal(1, game.HeldRounds);
        Assert.Null(game.Winner);
        Assert.Single(game.Actions, a => a.Kind == ActionKind.ObjectiveReset);
    }

    [Fact]
    public void Holder_death_resets_before_defeat_without_awarding_a_round()
    {
        var game = new Game(new BattleMap(Scenarios.PlainTiles()),
            [new Infantry(1, Side.Human, new(0,0)), new Armor(2, Side.Computer, new(1,0))],
            mission: MissionType.Rivergate, objective: new(0,0));
        Scenarios.NextHumanTurn(game);
        var saved = game.ExportSnapshot();
        saved.Units[0] = saved.Units[0] with { Health = 1 };
        game = Game.Restore(saved);
        Assert.True(game.EndTurn(Side.Human).IsSuccess);
        Assert.True(game.Attack(Side.Computer, 2, 1).IsSuccess);
        Assert.Equal(0, game.HeldRounds);
        Assert.Equal(2, game.LastEvaluatedComputerTurn);
        Assert.Equal(Side.Computer, game.Winner);
        Assert.Single(game.Actions, a => a.Kind == ActionKind.ObjectiveReset);
        Assert.Single(game.Actions, a => a.Kind == ActionKind.MissionFailed);
    }

    [Fact]
    public void Destroyed_holder_resets_even_when_other_friendly_units_survive()
    {
        var game = Quiet();
        var saved = game.ExportSnapshot();
        saved.Units[1] = saved.Units[1] with { Position = new(1,0) };
        saved = saved with { Units = [saved.Units[0] with { Health = 1 }, saved.Units[1],
            new UnitSnapshot("Infantry", 3, Side.Human, new(4,4),10,false,false)] };
        game = Game.Restore(saved);
        Scenarios.NextHumanTurn(game);
        game.EndTurn(Side.Human);
        Assert.True(game.Attack(Side.Computer,2,1).IsSuccess);
        Assert.Equal(0,game.HeldRounds);
        Assert.Null(game.Winner);
        game.EndTurn(Side.Computer);
        Assert.Equal(0,game.HeldRounds);
    }

    [Fact]
    public void Opponent_occupancy_never_counts_as_friendly_capture()
    {
        var game = new Game(new BattleMap(Scenarios.PlainTiles()),
            [new Infantry(1, Side.Human, new(0,0)), new Infantry(2, Side.Computer, new(3,3))],
            mission: MissionType.Rivergate, objective: new(3,3));
        Scenarios.NextHumanTurn(game); Scenarios.NextHumanTurn(game);
        Assert.Equal(0,game.HeldRounds); Assert.Null(game.Winner);
        Assert.Equal(4,game.LastEvaluatedComputerTurn);
    }

    [Fact]
    public void Eliminating_opponents_still_requires_capture_and_empty_computer_turns_finish()
    {
        var game = new Game(new BattleMap(Scenarios.PlainTiles()),
            [new Armor(1, Side.Human, new(0,0)), new Infantry(2, Side.Computer, new(1,0))],
            mission: MissionType.Rivergate, objective: new(3,0));
        var saved=game.ExportSnapshot(); saved.Units[1]=saved.Units[1] with {Health=1}; game=Game.Restore(saved);
        Assert.True(game.Attack(Side.Human,1,2).IsSuccess); Assert.Null(game.Winner);
        game.EndTurn(Side.Human); Assert.True(new ComputerOpponent().PlayTurn(game).IsSuccess);
        Assert.True(game.Move(Side.Human,1,new(3,0)).IsSuccess);
        for(var i=0;i<2;i++) { game.EndTurn(Side.Human); Assert.True(new ComputerOpponent().PlayTurn(game).IsSuccess); }
        Assert.Equal(Side.Human,game.Winner); Assert.Equal(2,game.HeldRounds);
    }

    [Fact]
    public void Snapshot_round_trip_preserves_progress_and_never_evaluates_on_restore()
    {
        var game=Quiet(); Scenarios.NextHumanTurn(game);
        var json=JsonSerializer.Serialize(game.ExportSnapshot());
        var restored=Game.Restore(JsonSerializer.Deserialize<GameSnapshot>(json)!);
        Assert.Equal(json,JsonSerializer.Serialize(restored.ExportSnapshot())); Assert.Empty(restored.Actions);
        Assert.Equal(1,restored.HeldRounds); Assert.Equal(2,restored.LastEvaluatedComputerTurn);
        Assert.Equal(GameError.NotYourTurn,restored.EndTurn(Side.Computer).Error);
        Scenarios.NextHumanTurn(restored);
        Assert.Equal(Side.Human,restored.Winner);
        Assert.Equal(Side.Human,Game.Restore(restored.ExportSnapshot()).Winner);
    }

    [Fact]
    public void Old_json_without_mission_fields_and_default_games_keep_elimination()
    {
        var snapshot=Game.CreateDefault().ExportSnapshot();
        foreach(var unit in snapshot.Units.Where(u=>u.Side==Side.Computer).ToArray())
            snapshot.Units[Array.IndexOf(snapshot.Units,unit)]=unit with {Health=0};
        var oldJson=JsonSerializer.Serialize(new {snapshot.CurrentSide,snapshot.TurnNumber,snapshot.Tiles,snapshot.Units});
        var old=Game.Restore(JsonSerializer.Deserialize<GameSnapshot>(oldJson)!);
        Assert.Equal(MissionType.Elimination,old.Mission); Assert.Null(old.Objective);
        Assert.Equal(Side.Human,old.Winner); Assert.Equal(0,old.HeldRounds);
        Assert.Equal(MissionType.Elimination,Game.CreateDefault().Mission);
    }

    [Fact]
    public void Default_mission_has_a_real_winning_route_against_deterministic_opposition()
    {
        var game=Game.CreateRivergate();
        void Move(int unit,int x,int y)=>Assert.True(game.Move(Side.Human,unit,new(x,y)).IsSuccess);
        void Attack(int unit,int target)=>Assert.True(game.Attack(Side.Human,unit,target).IsSuccess);
        void Round(){ Assert.True(game.EndTurn(Side.Human).IsSuccess); Assert.True(new ComputerOpponent().PlayTurn(game).IsSuccess); }
        Move(3,2,1); Move(2,5,0); Move(1,3,0); Round();
        Move(2,5,1); Move(1,5,0); Move(3,3,1); Round();
        Assert.Contains(game.Units,u=>u.Side==Side.Computer && u.IsAlive && u.Position==game.Objective);
        Assert.True(game.Units.Single(u=>u.Id==1).Health<10); // Real opponent pressure.
        Move(2,5,2); Move(1,6,2); Attack(1,4); Move(3,4,0); Attack(3,5); Attack(2,5); Round();
        Attack(2,5); Move(3,6,0); Attack(3,4); Round();
        Attack(2,4); Move(3,6,2); Attack(3,6); Round();
        Attack(3,6); Round();
        Assert.Null(game.Winner); Assert.Equal(0,game.HeldRounds);
        Move(3,5,3); Round(); Assert.Equal(1,game.HeldRounds);
        Round(); Assert.Equal(Side.Human,game.Winner); Assert.Equal(2,game.HeldRounds);
        Assert.IsType<Artillery>(game.OutpostHolder);
        Assert.Equal(2,game.Units.Count(u=>u.Side==Side.Human && !u.IsAlive));
        Assert.Contains(game.Actions,a=>a.Side==Side.Computer && a.Kind==ActionKind.Attack && a.Damage>0);
    }

    [Fact]
    public void Invalid_mission_state_and_water_objective_are_rejected()
    {
        var snapshot=Game.CreateRivergate().ExportSnapshot();
        Assert.Throws<ArgumentException>(()=>Game.Restore(snapshot with {HeldRounds=3}));
        Assert.Throws<ArgumentException>(()=>Game.Restore(snapshot with {HeldRounds=1}));
        var quiet=Quiet().ExportSnapshot();
        Assert.Throws<ArgumentException>(()=>Game.Restore(quiet with {TurnNumber=3,HeldRounds=2,LastEvaluatedComputerTurn=2}));
        Assert.Throws<ArgumentException>(()=>Game.Restore(snapshot with {LastEvaluatedComputerTurn=1}));
        Assert.Throws<ArgumentException>(()=>Game.Restore(snapshot with {Objective=new(4,3)}));
    }

    [Fact]
    public void F4_is_unoccupied_land_reachable_by_every_starting_unit_using_engine_moves()
    {
        var initial=Game.CreateRivergate(); var goal=initial.Objective!.Value;
        Assert.Equal(new Position(5,3),goal); Assert.NotEqual(Terrain.Water,initial.Map.GetTerrain(goal));
        Assert.DoesNotContain(initial.Units,u=>u.Position==goal);
        foreach(var unit in initial.Units)
        {
            var queue=new Queue<GameSnapshot>(); var seen=new HashSet<Position>{unit.Position};
            queue.Enqueue(initial.ExportSnapshot()); var found=false;
            while(queue.TryDequeue(out var snapshot) && !found)
            {
                var game=Game.Restore(snapshot);
                if(game.CurrentSide!=unit.Side) game.EndTurn(game.CurrentSide);
                foreach(var move in game.GetLegalMoves(unit.Side,unit.Id))
                {
                    if(move.Key==goal) {found=true;break;}
                    if(!seen.Add(move.Key))continue;
                    var next=Game.Restore(game.ExportSnapshot());
                    Assert.True(next.Move(unit.Side,unit.Id,move.Key).IsSuccess);
                    next.EndTurn(unit.Side); next.EndTurn(next.CurrentSide);
                    queue.Enqueue(next.ExportSnapshot());
                }
            }
            Assert.True(found,$"{unit.Side} {unit.GetType().Name} cannot reach F4");
        }
    }
}
