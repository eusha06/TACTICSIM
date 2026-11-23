using static TacticSim.Sim.Tests.World;

namespace TacticSim.Sim.Tests;

public class MovementResolutionTests
{
    private static readonly GameContent Open = With(World.Open());
    private static readonly GameContent ForestLane = With([".........", ".........", ".F.......", ".........", "........."]);

    [Fact]
    public void Units_arrive_one_open_hex_per_tick()
    {
        var s = State(Open, Blue("rifle", 0, 2), Red("rifle", 8, 4));
        var r = Resolve(Open, s, Move(1, (1, 2), (2, 2), (3, 2)));
        Assert.Equal([1, 2, 3], r.Of(EventKind.Move).Select(x => x.Tick));
        Assert.Equal(new Hex(3, 2), r.U(1).Pos);
        Assert.Equal(new Hex(0, 2), s.Unit(1)!.Pos); // the input state is never changed
    }

    [Fact]
    public void A_cost_two_hex_takes_two_ticks()
    {
        var s = State(ForestLane, Blue("rifle", 0, 2), Red("rifle", 8, 4));
        var r = Resolve(ForestLane, s, Move(1, (1, 2)));
        var move = Assert.Single(r.Of(EventKind.Move));
        Assert.Equal((2, 2), (move.Tick, move.Event.Ticks));
    }

    [Fact]
    public void Two_units_entering_the_same_hex_on_the_same_tick_both_stay()
    {
        var s = State(Open, Blue("rifle", 0, 2), Red("rifle", 2, 2));
        var r = Resolve(Open, s, Move(1, (1, 2)), Move(2, (1, 2)));
        Assert.Equal(2, r.Of(EventKind.Blocked).Count(x => x.Tick == 1 && x.Event.Reason == "collision"));
        Assert.Equal((new Hex(0, 2), new Hex(2, 2)), (r.U(1).Pos, r.U(2).Pos));
    }

    [Fact]
    public void Entering_an_enemy_hex_stops_the_mover()
    {
        // Red's rifle reaches the forest on tick 2; Blue's armor would arrive on tick 3.
        var s = State(ForestLane, Blue("armor", 0, 2), Red("rifle", 2, 2));
        var r = Resolve(ForestLane, s, Move(1, (1, 2)), Move(2, (1, 2)));
        var blocked = Assert.Single(r.Of(EventKind.Blocked));
        Assert.Equal((3, "enemy", 2), (blocked.Tick, blocked.Event.Reason, blocked.Event.TargetId));
        Assert.Equal(new Hex(0, 2), r.U(1).Pos);
        Assert.Equal(new Hex(1, 2), r.U(2).Pos);
    }

    [Fact]
    public void Friendly_units_pass_through_each_other()
    {
        var s = State(Open, Blue("rifle", 0, 2), Blue("rifle", 1, 2), Red("rifle", 8, 4));
        var r = Resolve(Open, s, Move(1, (1, 2), (2, 2), (3, 2)));
        Assert.Equal(new Hex(3, 2), r.U(1).Pos);
        Assert.Empty(r.Of(EventKind.Blocked));
    }

    [Fact]
    public void A_unit_whose_final_hex_is_taken_by_a_friendly_stops_at_the_last_free_hex()
    {
        var s = State(Open, Blue("rifle", 0, 2), Blue("rifle", 3, 2), Red("rifle", 8, 4));
        var r = Resolve(Open, s, Move(1, (1, 2), (2, 2)), Move(2, (2, 2)));
        var blocked = Assert.Single(r.Of(EventKind.Blocked));
        Assert.Equal((2, "friendly"), (blocked.Tick, blocked.Event.Reason));
        Assert.Equal((new Hex(1, 2), new Hex(2, 2)), (r.U(1).Pos, r.U(2).Pos));
    }

    [Fact]
    public void Contact_with_a_visible_enemy_ends_the_path()
    {
        var s = State(Open, Blue("rifle", 0, 2), Red("rifle", 4, 2));
        var r = Resolve(Open, s, Move(1, (1, 2), (2, 2), (3, 2), (4, 2)));
        var contact = Assert.Single(r.Of(EventKind.Contact));
        Assert.Equal((3, 2), (contact.Tick, contact.Event.TargetId!.Value));
        Assert.Equal(new Hex(3, 2), r.U(1).Pos);
        Assert.Contains(r.Of(EventKind.Spotted), x => x.Event.Side == Side.Blue && x.Event.TargetId == 2 && x.Tick == 1);
    }

    [Fact]
    public void A_unit_stopped_on_a_friendly_hex_steps_back()
    {
        var s = State(Open, Blue("rifle", 0, 2), Blue("rifle", 2, 2), Red("rifle", 3, 2));
        var r = Resolve(Open, s, Move(1, (1, 2), (2, 2), (2, 1)));
        Assert.Single(r.Of(EventKind.Contact));
        var giveWay = Assert.Single(r.Of(EventKind.GiveWay));
        Assert.Equal((new Hex(2, 2), new Hex(1, 2)), (giveWay.Event.From!.Value, giveWay.Event.To!.Value));
        Assert.Equal(new Hex(1, 2), r.U(1).Pos);
    }
}

public class FireResolutionTests
{
    private static readonly GameContent Open = With(World.Open());

    [Fact]
    public void Simultaneous_fire_allows_mutual_kills()
    {
        var s = State(Open, Blue("rifle", 0, 2), Red("rifle", 2, 2));
        s.Unit(1)!.Hp = 3;
        s.Unit(2)!.Hp = 3;
        var r = Resolve(Open, s, new Order(1, OrderType.Attack, TargetId: 2), new Order(2, OrderType.Attack, TargetId: 1));
        Assert.Equal(2, r.Of(EventKind.Destroyed).Count(x => x.Tick == 1));
        Assert.Equal((MatchResult.Draw, "annihilation"), (r.FinalState.Result!.Value, r.FinalState.EndReason!));
        Assert.Equal(1, r.FinalState.BlueKills);
        Assert.Equal(1, r.FinalState.RedKills);
    }

    [Fact]
    public void Overwatch_fires_on_the_tick_an_enemy_enters_range_and_only_once()
    {
        var s = State(Open, Blue("at", 0, 2), Red("rifle", 6, 2));
        var r = Resolve(Open, s, new Order(1, OrderType.Overwatch), Move(2, (5, 2), (4, 2), (3, 2), (2, 2)));
        var shot = Assert.Single(r.Of(EventKind.Shot), x => x.Event.UnitId == 1);
        Assert.Equal((3, 2, "overwatch"), (shot.Tick, shot.Event.Damage, shot.Event.Reason!));
        Assert.Equal(8, r.U(2).Hp);
    }

    [Fact]
    public void Overwatch_with_no_enemy_in_range_does_not_fire()
    {
        var s = State(Open, Blue("at", 0, 2), Red("rifle", 8, 2));
        var r = Resolve(Open, s, new Order(1, OrderType.Overwatch));
        Assert.DoesNotContain(r.Of(EventKind.Shot), x => x.Event.UnitId == 1);
    }

    [Fact]
    public void Attack_fires_the_first_tick_the_target_is_in_range_then_stops()
    {
        var s = State(Open, Blue("armor", 0, 2), Red("rifle", 6, 2));
        var r = Resolve(Open, s, new Order(1, OrderType.Attack, Path((1, 2), (2, 2), (3, 2), (4, 2), (5, 2)), TargetId: 2));
        var shot = Assert.Single(r.Of(EventKind.Shot), x => x.Event.UnitId == 1);
        Assert.Equal((3, 5), (shot.Tick, shot.Event.Damage)); // target holds: 6 - 1
        Assert.Equal(new Hex(3, 2), r.U(1).Pos);
    }

    [Fact]
    public void Attack_that_never_reaches_range_does_nothing_else()
    {
        var s = State(Open, Blue("armor", 0, 2), Red("rifle", 8, 2), Red("recon", 2, 2));
        var r = Resolve(Open, s, new Order(1, OrderType.Attack, Path((1, 2)), TargetId: 2));
        Assert.DoesNotContain(r.Events(), x => x.Event.UnitId == 1 && x.Event.Kind == EventKind.Shot);
        Assert.Contains(r.Of(EventKind.Shot), x => x.Event.UnitId == 3 && x.Tick == 9); // recon still fires at will
    }

    [Fact]
    public void Hold_fires_at_will_after_the_last_tick_with_one_extra_cover()
    {
        var s = State(Open, Blue("rifle", 0, 2), Red("rifle", 2, 2));
        var r = Resolve(Open, s);
        Assert.Equal(2, r.Of(EventKind.Shot).Count(x => x.Tick == 9 && x.Event.Damage == 3 && x.Event.Reason == "fireAtWill"));
    }

    [Fact]
    public void Move_fires_at_will_at_its_assigned_target_or_the_best_one()
    {
        var s = State(Open, Blue("armor", 0, 2), Red("rifle", 4, 2), Red("recon", 3, 2));
        var best = Resolve(Open, s, Move(1, (1, 2)));
        Assert.Equal(2, Assert.Single(best.Of(EventKind.Shot), x => x.Event.UnitId == 1).Event.TargetId);
        var assigned = Resolve(Open, s, new Order(1, OrderType.Move, Path((1, 2)), TargetId: 3));
        Assert.Equal(3, Assert.Single(assigned.Of(EventKind.Shot), x => x.Event.UnitId == 1).Event.TargetId);
    }

    [Fact]
    public void Artillery_that_moves_never_fires_but_holding_artillery_fires_indirectly()
    {
        var s = State(Open, Blue("artillery", 0, 2), Blue("recon", 2, 2), Red("rifle", 5, 2));
        var moved = Resolve(Open, s, Move(1, (1, 2)));
        Assert.DoesNotContain(moved.Events(), x => x.Event.UnitId == 1 && x.Event.Kind == EventKind.Shell);
        var held = Resolve(Open, s);
        var shell = Assert.Single(held.Of(EventKind.Shell));
        Assert.Equal((9, 1, 5), (shell.Tick, shell.Event.UnitId!.Value, shell.Event.Damage)); // 6 - hold
    }

    [Fact]
    public void Barrage_lands_on_tick_six_and_hits_whatever_is_on_the_hex()
    {
        var s = State(Open, Blue("artillery", 0, 2), Red("rifle", 4, 2));
        var barrage = new Order(1, OrderType.Barrage, TargetHex: new Hex(4, 2));
        var hit = Assert.Single(Resolve(Open, s, barrage).Of(EventKind.Barrage));
        Assert.Equal((6, 2, 5), (hit.Tick, hit.Event.TargetId!.Value, hit.Event.Damage));

        var miss = Assert.Single(Resolve(Open, s, barrage, Move(2, (5, 2))).Of(EventKind.Barrage));
        Assert.Null(miss.Event.TargetId);

        var withFriend = State(Open, Blue("artillery", 0, 2), Blue("rifle", 3, 2), Red("rifle", 8, 4));
        var friendlyFire = Assert.Single(Resolve(Open, withFriend, barrage, Move(2, (4, 2))).Of(EventKind.Barrage));
        Assert.Equal((2, 6), (friendlyFire.Event.TargetId!.Value, friendlyFire.Event.Damage));
    }

    [Fact]
    public void Barrage_does_not_land_if_the_artillery_died_first()
    {
        var s = State(Open, Blue("artillery", 0, 2), Red("armor", 2, 2));
        s.Unit(1)!.Hp = 1;
        var r = Resolve(Open, s, new Order(1, OrderType.Barrage, TargetHex: new Hex(5, 2)), new Order(2, OrderType.Attack, TargetId: 1));
        Assert.Empty(r.Of(EventKind.Barrage));
        Assert.Contains(r.Of(EventKind.Destroyed), x => x.Tick == 1 && x.Event.TargetId == 1);
    }
}

public class ScoringTests
{
    private static readonly GameContent TwoObjectives = With(World.Open(), ("A", 4, 2), ("B", 6, 4));

    private static TurnResult Hold(MatchState s) => Resolve(TwoObjectives, s);

    [Fact]
    public void A_lone_capturing_unit_controls_and_scores()
    {
        var s = State(TwoObjectives, Blue("rifle", 4, 2), Red("rifle", 0, 0));
        var r = Hold(s);
        Assert.Equal(Side.Blue, r.FinalState.Control["A"]);
        Assert.Equal((1, 0, 1), (r.FinalState.VpBlue, r.FinalState.VpRed, r.VpBlue));
        Assert.Contains(r.EndEvents, e => e.Kind == EventKind.Control && e.Reason == "captured" && e.Objective == "A");
        Assert.Equal(2, r.FinalState.Turn);
        Assert.Equal(new VpPoint(1, 1, 0), Assert.Single(r.FinalState.VpHistory));
    }

    [Fact]
    public void An_adjacent_enemy_contests_and_nobody_scores()
    {
        var s = State(TwoObjectives, Blue("rifle", 4, 2), Red("rifle", 5, 2));
        s.Unit(1)!.Hp = 99; s.Unit(2)!.Hp = 99;
        var r = Hold(s);
        Assert.Null(r.FinalState.Control["A"]);
        Assert.Equal((0, 0), (r.FinalState.VpBlue, r.FinalState.VpRed));
    }

    [Fact]
    public void Contested_and_lost_control_are_reported()
    {
        var s = State(TwoObjectives, Blue("rifle", 4, 2), Red("rifle", 5, 2), Red("recon", 8, 0));
        s.Control["A"] = Side.Blue;
        s.Unit(1)!.Hp = 99; s.Unit(2)!.Hp = 99;
        Assert.Contains(Hold(s).EndEvents, e => e.Kind == EventKind.Control && e.Reason == "contested");
        var empty = State(TwoObjectives, Blue("rifle", 0, 0), Red("rifle", 8, 0));
        empty.Control["A"] = Side.Red;
        Assert.Contains(Hold(empty).EndEvents, e => e.Kind == EventKind.Control && e.Reason == "lost" && e.Side == null);
    }

    [Fact]
    public void Recon_and_artillery_cannot_capture()
    {
        var s = State(TwoObjectives, Blue("recon", 4, 2), Blue("artillery", 6, 4), Red("rifle", 0, 0));
        var r = Hold(s);
        Assert.All(r.FinalState.Control.Values, v => Assert.Null(v));
    }

    [Fact]
    public void Destroying_every_enemy_wins()
    {
        var s = State(TwoObjectives, Blue("armor", 0, 2), Red("rifle", 2, 2));
        s.Unit(2)!.Hp = 1;
        var r = Hold(s);
        Assert.Equal((MatchResult.BlueWins, "annihilation"), (r.FinalState.Result!.Value, r.FinalState.EndReason!));
        Assert.Contains(r.EndEvents, e => e.Kind == EventKind.Result && e.Side == Side.Blue);
        Assert.Equal(1, r.FinalState.Turn);
        Assert.Throws<InvalidOperationException>(() => Hold(r.FinalState));
    }

    [Fact]
    public void Reaching_the_vp_target_wins_and_a_shared_finish_goes_to_the_higher_score()
    {
        var s = State(TwoObjectives, Blue("rifle", 4, 2), Red("rifle", 6, 4));
        s.VpBlue = 11; s.VpRed = 10;
        Assert.Equal(MatchResult.BlueWins, Hold(s).FinalState.Result);
        s.VpRed = 11;
        var level = Hold(s).FinalState;
        Assert.Equal((MatchResult.Draw, "vp", 12, 12), (level.Result!.Value, level.EndReason!, level.VpBlue, level.VpRed));
        var both = State(TwoObjectives, Blue("rifle", 4, 2), Blue("rifle", 6, 4), Red("rifle", 0, 0));
        both.VpBlue = 10; both.VpRed = 11;
        Assert.Equal(MatchResult.BlueWins, Hold(both).FinalState.Result); // 12 beats 11
    }

    [Fact]
    public void After_the_last_turn_the_leader_wins_and_a_tie_is_a_draw()
    {
        var s = State(TwoObjectives, Blue("rifle", 0, 0), Red("rifle", 8, 0));
        s.Turn = 12; s.VpBlue = 3; s.VpRed = 2;
        var r = Hold(s).FinalState;
        Assert.Equal((MatchResult.BlueWins, "turnLimit", 12), (r.Result!.Value, r.EndReason!, r.Turn));
        s.VpRed = 3;
        Assert.Equal(MatchResult.Draw, Hold(s).FinalState.Result);
        s.VpRed = 5;
        Assert.Equal(MatchResult.RedWins, Hold(s).FinalState.Result);
    }
}
