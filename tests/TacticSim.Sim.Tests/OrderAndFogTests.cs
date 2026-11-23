using System.Text.Json;
using static TacticSim.Sim.Tests.World;

namespace TacticSim.Sim.Tests;

public class OrderValidationTests
{
    private static readonly GameContent C = With([".........", ".........", ".F.......", ".........", "........."]);

    // 1 rifle A3, 2 artillery B1, 3 armor C4 | 4 red rifle C3 (visible), 5 red rifle I5 (hidden)
    private static MatchState S() => State(C, Blue("rifle", 0, 2), Blue("artillery", 1, 0), Blue("armor", 2, 3),
        Red("rifle", 2, 2), Red("rifle", 8, 4));

    private static string? Error(params Order[] orders) => OrderRules.Validate(C, S(), Side.Blue, orders).Error;

    [Fact]
    public void Units_without_orders_hold()
    {
        var check = OrderRules.Validate(C, S(), Side.Blue, null);
        Assert.True(check.Ok);
        Assert.Equal([1, 2, 3], check.Orders.Select(o => o.UnitId));
        Assert.All(check.Orders, o => Assert.Equal(OrderType.Hold, o.Type));
    }

    [Fact]
    public void Legal_orders_pass_and_are_normalized()
    {
        var check = OrderRules.Validate(C, S(), Side.Blue,
        [
            new Order(1, OrderType.Move, Path((1, 2)), TargetId: 4),
            new Order(2, OrderType.Barrage, Path((1, 1)), TargetHex: new Hex(4, 2)),
            new Order(3, OrderType.Overwatch, TargetId: 4)
        ]);
        Assert.True(check.Ok, check.Error);
        Assert.Null(check.Orders[1].Path);
        Assert.Null(check.Orders[2].TargetId);
    }

    public static TheoryData<Order[], string> Rejections => new()
    {
        { [new Order(4, OrderType.Hold)], "You can only give orders to your own units." },
        { [new Order(99, OrderType.Hold)], "Unit #99 is not on the battlefield." },
        { [Order.Hold(1), new Order(1, OrderType.Overwatch)], "Rifle Infantry (A3) has more than one order. Each unit gets exactly one." },
        { [new Order(1, OrderType.Move)], "Move needs a destination hex." },
        { [new Order(2, OrderType.Move, Path((0, 1)), TargetId: 4)], "Artillery can't fire after moving." },
        { [new Order(2, OrderType.Attack, Path((0, 1)), TargetId: 4)], "Artillery can't fire after moving." },
        { [new Order(1, OrderType.Attack, TargetId: 5)], "That enemy isn't in sight right now. Pick a visible enemy." },
        { [new Order(1, OrderType.Attack)], "Attack needs a target: pick a visible enemy." },
        { [new Order(2, OrderType.Attack, TargetId: 4)], "Out of range: needs 3–6, target is 2." },
        { [new Order(1, OrderType.Attack, TargetId: 4)], "No line of sight: Forest at B3 blocks it." },
        { [new Order(1, OrderType.Attack, Path((1, 2), (2, 2)), TargetId: 4)], "C3 is held by an enemy unit." },
        { [new Order(1, OrderType.Barrage, TargetHex: new Hex(4, 2))], "Only Artillery can Barrage." },
        { [new Order(2, OrderType.Barrage)], "Barrage needs a target hex on the map." },
        { [new Order(2, OrderType.Barrage, TargetHex: new Hex(2, 0))], "Out of range: needs 3–6, target is 1." },
        { [new Order(2, OrderType.Barrage, TargetHex: new Hex(7, 0))], "Barrage needs a hex your units can see. H1 is in the fog." },
        { [Move(1, (0, 3)), Move(3, (1, 3), (0, 3))], "Rifle Infantry (A3) and Armor (C4) can't both end on A4." },
        { [Move(1, (0, 3), (1, 3), (2, 3))], "Rifle Infantry (A3) and Armor (C4) can't both end on C4." },
    };

    [Theory, MemberData(nameof(Rejections))]
    public void Illegal_orders_are_rejected_in_plain_english(Order[] orders, string expected) => Assert.Equal(expected, Error(orders));

    [Fact]
    public void A_finished_match_takes_no_orders()
    {
        var s = S();
        s.Result = MatchResult.Draw;
        Assert.Equal("This match is over.", OrderRules.Validate(C, s, Side.Blue, []).Error);
    }
}

public class FogTests
{
    private static readonly GameContent Open = With(World.Open());

    [Fact]
    public void State_omits_enemies_the_viewer_cannot_see()
    {
        var c = V1;
        var start = Scenario.Create(c, "rivergate");
        var blue = Fog.StateFor(c, start, Side.Blue);
        Assert.All(blue.Units, u => Assert.Equal(Side.Blue, u.Side));
        Assert.Equal(6, blue.Units.Length);
        Assert.Empty(blue.LastSeen);
        Assert.False(blue.FogLifted);
        Assert.DoesNotContain("\"red\"", JsonSerializer.Serialize(blue.Units, SimJson.Options));
    }

    [Fact]
    public void Replay_hides_enemy_moves_until_they_come_into_sight()
    {
        var s = State(Open, Blue("rifle", 0, 2), Red("recon", 8, 2));
        var r = Resolve(Open, s, Move(2, (7, 2), (6, 2), (5, 2), (4, 2), (3, 2)));
        var replay = Fog.ReplayFor(Open, r, Side.Blue);
        // Recon reaches C3 (distance 3) on tick 5. Before that Blue must learn nothing about it.
        foreach (var tick in replay.Ticks.Where(t => t.Tick < 5))
        {
            Assert.DoesNotContain(tick.Units, u => u.Id == 2);
            Assert.DoesNotContain(tick.Events, e => e.UnitId == 2 || e.TargetId == 2);
        }
        var appear = Assert.Single(replay.Ticks.Single(t => t.Tick == 5).Events, e => e.Kind == EventKind.Move);
        Assert.Null(appear.From); // stepped out of the fog: origin stays hidden
        Assert.Contains(replay.Ticks.Single(t => t.Tick == 5).Events, e => e.Kind == EventKind.Spotted && e.TargetId == 2);
        Assert.Contains("Spotted: enemy Recon at D3.", replay.Debrief);
        Assert.Contains(Fog.ReplayFor(Open, r, Side.Red).Ticks.First().Events, e => e.Kind == EventKind.Move && e.From is not null);
    }

    [Fact]
    public void Fire_from_an_unseen_shooter_hides_the_shooter()
    {
        // Red recon (vision 5) spots for red artillery; neither is visible to Blue's rifle (vision 3).
        var s = State(Open, Blue("rifle", 0, 2), Red("recon", 4, 2), Red("artillery", 6, 2));
        var r = Resolve(Open, s, new Order(3, OrderType.Attack, TargetId: 1));
        var blue = Fog.ReplayFor(Open, r, Side.Blue);
        var shell = Assert.Single(blue.Ticks.SelectMany(t => t.Events), e => e.Kind == EventKind.Shell);
        Assert.Equal((null, null, 1, 5), (shell.UnitId, shell.From, shell.TargetId!.Value, shell.Damage)); // 6 - hold
        Assert.All(blue.Ticks, t => Assert.DoesNotContain(t.Units, u => u.Side == Side.Red));
        var red = Fog.ReplayFor(Open, r, Side.Red);
        Assert.Equal(3, red.Ticks.SelectMany(t => t.Events).Single(e => e.Kind == EventKind.Shell).UnitId);
        Assert.Contains("Damage: you dealt 0 and took 5.", blue.Debrief);
    }

    [Fact]
    public void Barrage_on_a_hidden_unit_shows_only_the_shell()
    {
        // The resolver trusts validated orders; here the shell lands on a hex Blue cannot see by tick 6.
        var s = State(Open, Blue("artillery", 0, 2), Red("rifle", 4, 0));
        var r = Resolve(Open, s, new Order(1, OrderType.Barrage, TargetHex: new Hex(4, 2)), Move(2, (4, 1), (4, 2)));
        var shell = Assert.Single(Fog.ReplayFor(Open, r, Side.Blue).Ticks.SelectMany(t => t.Events), e => e.Kind == EventKind.Barrage);
        Assert.Equal((1, null, 0), (shell.UnitId!.Value, shell.TargetId, shell.Damage));
        Assert.Equal(4, r.U(2).Hp);
    }

    [Fact]
    public void Enemies_that_slip_out_of_sight_leave_last_seen_markers()
    {
        var s = State(Open, Blue("rifle", 0, 2), Red("recon", 3, 2));
        var r = Resolve(Open, s, Move(2, (4, 2), (5, 2), (6, 2)));
        var marker = Assert.Single(r.FinalState.BlueLastSeen);
        Assert.Equal((2, "recon", new Hex(3, 2)), (marker.UnitId, marker.Type, marker.Pos));
        Assert.Contains(r.Of(EventKind.Lost), x => x.Event.Side == Side.Blue && x.Event.To == new Hex(3, 2));
        var view = Fog.StateFor(Open, r.FinalState, Side.Blue);
        Assert.DoesNotContain(view.Units, u => u.Id == 2);
        Assert.Single(view.LastSeen);
    }

    [Fact]
    public void After_the_match_the_fog_lifts()
    {
        var s = State(Open, Blue("armor", 0, 2), Red("rifle", 2, 2), Red("recon", 8, 4));
        s.Turn = 12;
        var r = Resolve(Open, s);
        var view = Fog.StateFor(Open, r.FinalState, Side.Blue);
        Assert.True(view.FogLifted);
        Assert.Contains(view.Units, u => u.Id == 3);
        Assert.Equal(45, view.Visible.Length);
        Assert.StartsWith("Draw", Fog.ReplayFor(Open, r, Side.Blue).Debrief[0]);
    }
}

public class DeterminismTests
{
    private static readonly GameContent C = V1;

    private static (Order[] Blue, Order[] Red) OpeningOrders() =>
    (
        [Move(4, (1, 2), (2, 2), (3, 2), (4, 2), (5, 2), (6, 2), (7, 2)), Move(1, (2, 4), (2, 5)),
         new Order(6, OrderType.Move, Path((1, 8), (2, 8), (3, 8), (4, 8), (5, 8), (6, 8), (7, 8)))],
        [Move(10, (13, 2), (12, 2), (11, 2), (10, 2), (9, 2), (8, 2)), Move(7, (12, 4), (11, 4), (10, 5)),
         new Order(12, OrderType.Move, Path((13, 8), (12, 8), (11, 8), (10, 8), (9, 8), (8, 8)))]
    );

    private static string Json(object value) => JsonSerializer.Serialize(value, SimJson.Options);

    [Fact]
    public void Orders_used_here_are_legal()
    {
        var start = Scenario.Create(C, "rivergate");
        var (blue, red) = OpeningOrders();
        Assert.True(OrderRules.Validate(C, start, Side.Blue, blue).Ok, OrderRules.Validate(C, start, Side.Blue, blue).Error);
        Assert.True(OrderRules.Validate(C, start, Side.Red, red).Ok, OrderRules.Validate(C, start, Side.Red, red).Error);
    }

    [Fact]
    public void Same_state_and_orders_give_the_same_result()
    {
        var (blue, red) = OpeningOrders();
        var a = TurnResolver.Resolve(C, Scenario.Create(C, "rivergate"), blue, red);
        var b = TurnResolver.Resolve(C, Scenario.Create(C, "rivergate"), blue, red);
        Assert.Equal(Json(a), Json(b));
        Assert.NotEmpty(a.Of(EventKind.Move));
    }

    [Fact]
    public void Save_then_reload_resolves_identically()
    {
        var (blue, red) = OpeningOrders();
        var start = Scenario.Create(C, "rivergate");
        var first = TurnResolver.Resolve(C, start, blue, red);
        var saved = JsonSerializer.Deserialize<MatchState>(Json(first.FinalState), SimJson.Options)!;
        Assert.Equal(Json(first.FinalState), Json(saved));
        var again = TurnResolver.Resolve(C, saved, [], []);
        var direct = TurnResolver.Resolve(C, first.FinalState, [], []);
        Assert.Equal(Json(direct), Json(again));
        var orders = JsonSerializer.Deserialize<Order[]>(Json(blue), SimJson.Options)!;
        Assert.Equal(Json(first), Json(TurnResolver.Resolve(C, start, orders, red)));
    }

    [Fact]
    public void Foreign_orders_are_ignored_by_the_resolver()
    {
        var start = Scenario.Create(C, "rivergate");
        var plain = TurnResolver.Resolve(C, start, [], []);
        var sneaky = TurnResolver.Resolve(C, start, [Move(7, (12, 4))], []);
        Assert.Equal(Json(plain), Json(sneaky));
    }
}
