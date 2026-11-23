using System.Diagnostics;
using System.Text.Json;
using TacticSim.Sim.Ai;
using static TacticSim.Sim.Tests.World;

namespace TacticSim.Sim.Tests;

public class AiTests
{
    private static readonly GameContent C = V1;

    private static Order[] Plan(MatchState state, Side side, Difficulty level, int seed = 1) =>
        AiPlanner.Plan(C, Fog.StateFor(C, state, side), level, seed);

    private static MatchState PlayTurns(int turns, Difficulty level, int seed = 3)
    {
        var state = Scenario.Create(C, "rivergate", seed);
        for (var i = 0; i < turns && !state.IsOver; i++)
            state = TurnResolver.Resolve(C, state, Plan(state, Side.Blue, level, seed), Plan(state, Side.Red, level, seed + 1)).FinalState;
        return state;
    }

    [Theory]
    [InlineData(Difficulty.Recruit)]
    [InlineData(Difficulty.Veteran)]
    [InlineData(Difficulty.Commander)]
    public void Ai_orders_are_always_legal_on_the_real_state(Difficulty level)
    {
        var state = Scenario.Create(C, "rivergate", 5);
        while (!state.IsOver)
        {
            var blue = Plan(state, Side.Blue, level, 11);
            var red = Plan(state, Side.Red, level, 12);
            var blueCheck = OrderRules.Validate(C, state, Side.Blue, blue);
            var redCheck = OrderRules.Validate(C, state, Side.Red, red);
            Assert.True(blueCheck.Ok, blueCheck.Error);
            Assert.True(redCheck.Ok, redCheck.Error);
            state = TurnResolver.Resolve(C, state, blue, red).FinalState;
        }
        Assert.InRange(state.VpHistory.Count, 1, 12);
    }

    [Fact]
    public void Ai_cannot_see_through_the_fog()
    {
        // Same Blue view, different hidden Red positions: the plan must not change.
        var a = Scenario.Create(C, "rivergate");
        var b = Scenario.Create(C, "rivergate");
        b.Unit(7)!.Pos = new Hex(12, 6);
        b.Unit(10)!.Pos = new Hex(12, 2);
        Assert.Equal(JsonSerializer.Serialize(Fog.StateFor(C, a, Side.Blue)), JsonSerializer.Serialize(Fog.StateFor(C, b, Side.Blue)));
        foreach (var level in Enum.GetValues<Difficulty>())
            Assert.Equal(JsonSerializer.Serialize(Plan(a, Side.Blue, level)), JsonSerializer.Serialize(Plan(b, Side.Blue, level)));
    }

    [Fact]
    public void Same_view_and_seed_give_the_same_plan_and_recruit_varies_by_seed()
    {
        var state = PlayTurns(2, Difficulty.Veteran);
        Assert.Equal(JsonSerializer.Serialize(Plan(state, Side.Red, Difficulty.Commander, 4)),
            JsonSerializer.Serialize(Plan(state, Side.Red, Difficulty.Commander, 4)));
        var plans = Enumerable.Range(1, 8).Select(seed => JsonSerializer.Serialize(Plan(state, Side.Red, Difficulty.Recruit, seed)));
        Assert.True(plans.Distinct().Count() > 1);
    }

    [Fact]
    public void Ai_heads_for_objectives_and_takes_its_near_one_early()
    {
        var state = PlayTurns(3, Difficulty.Veteran);
        Assert.Equal(Side.Blue, state.Control["C"]);
        Assert.Equal(Side.Red, state.Control["B"]);
    }

    [Fact]
    public void Ai_shoots_a_visible_target_in_range()
    {
        var c = With(World.Open());
        var s = State(c, Blue("armor", 0, 2), Red("rifle", 2, 2));
        s.Unit(2)!.Hp = 3;
        var orders = AiPlanner.Plan(c, Fog.StateFor(c, s, Side.Blue), Difficulty.Veteran, 1);
        var result = TurnResolver.Resolve(c, s, orders, []);
        Assert.False(result.U(2).Alive);
    }

    [Fact]
    public void Boot_camp_opponent_holds_by_script()
    {
        var state = Scenario.Create(C, "bootcamp");
        Assert.All(Plan(state, Side.Red, Difficulty.Commander), o => Assert.Equal(OrderType.Hold, o.Type));
    }

    [Fact]
    public void Knowledge_drops_markers_that_are_now_visibly_empty()
    {
        var view = Fog.StateFor(C, Scenario.Create(C, "rivergate"), Side.Blue) with
        {
            LastSeen = [new LastSeenMarker(9, "at", new Hex(1, 3), 8, 1), new LastSeenMarker(8, "rifle", new Hex(13, 6), 10, 1)]
        };
        var world = AiPlanner.Knowledge(C, C.Board("rivergate"), view);
        Assert.DoesNotContain(world.Units, u => u.Id == 9);
        Assert.Contains(world.Units, u => u.Id == 8 && u.Side == Side.Red);
    }

    [Fact]
    public void Ai_plans_within_the_time_budget()
    {
        var state = PlayTurns(4, Difficulty.Veteran);
        Plan(state, Side.Red, Difficulty.Commander); // warm up the JIT
        var clock = Stopwatch.StartNew();
        for (var i = 0; i < 10; i++) Plan(state, Side.Red, Difficulty.Commander, i);
        Assert.True(clock.ElapsedMilliseconds / 10.0 < 150, $"Commander averaged {clock.ElapsedMilliseconds / 10.0} ms");
    }
}
