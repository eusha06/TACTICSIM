using System.Text.Json;
using TacticSim.Core;

namespace TacticSim.Tests;

public class RoutePresentationTests
{
    [Fact]
    public void Route_goes_around_water_and_living_occupants_in_four_directions()
    {
        var tiles = Scenarios.PlainTiles(); tiles[0, 1] = Terrain.Water;
        foreach (var water in new[] { true, false })
        {
            tiles[0, 1] = water ? Terrain.Water : Terrain.Plain;
            var game = new Game(new BattleMap(tiles), [new Armor(1, Side.Human, new(0, 0)),
                new Infantry(2, Side.Computer, water ? new(7, 7) : new(1, 0))]);
            var route = game.GetLegalRoute(Side.Human, 1, new(2, 0));
            Assert.Equal(new Position(0, 0), route[0]); Assert.Equal(new Position(2, 0), route[^1]);
            Assert.Equal(5, route.Count); Assert.DoesNotContain(new Position(1, 0), route);
            Assert.All(route.Zip(route.Skip(1)), pair => Assert.Equal(1, pair.First.ManhattanDistanceTo(pair.Second)));
            Assert.True(game.Move(Side.Human, 1, new(2, 0)).IsSuccess);
            Assert.Equal(route, game.Actions.Single().Route);
        }
    }

    [Fact]
    public void All_exposed_routes_have_the_actual_Dijkstra_cost_and_emit_no_events_on_query()
    {
        var game = Game.CreateRivergate();
        foreach (var unit in game.Units.Where(u => u.Side == Side.Human))
        foreach (var destination in game.GetLegalMoves(Side.Human, unit.Id))
        {
            var route = game.GetLegalRoute(Side.Human, unit.Id, destination.Key);
            Assert.Equal(destination.Value, route.Skip(1).Sum(p => unit.GetMovementCost(game.Map.GetTerrain(p))!.Value));
            Assert.All(route.Skip(1), p => Assert.DoesNotContain(game.Units, u => u.IsAlive && u.Position == p));
        }
        Assert.Empty(game.Actions);
        Assert.Empty(game.GetLegalRoute(Side.Computer, 4, new(7, 6)));
        Assert.Empty(game.GetLegalRoute(Side.Human, 1, new(1, 0)));
    }

    [Fact]
    public void Frames_record_actual_health_destruction_and_action_flags_without_later_mutation()
    {
        var game = Scenarios.Duel(new Armor(1, Side.Human, new(0, 0)), new Artillery(2, Side.Computer, new(2, 0)));
        game.Move(Side.Human, 1, new(1, 0));
        var moved = game.Actions.Single(); var original = JsonSerializer.Serialize(moved.Snapshot);
        game.Attack(Side.Human, 1, 2); Scenarios.NextHumanTurn(game); game.Attack(Side.Human, 1, 2);
        Assert.Equal(original, JsonSerializer.Serialize(moved.Snapshot));
        Assert.True(moved.Snapshot!.Units.Single(u => u.Id == 1).HasMoved);
        var hit = game.Actions.Last(); Assert.Equal(2, hit.Damage); Assert.True(hit.Destroyed);
        Assert.Equal(0, hit.Snapshot!.Units.Single(u => u.Id == 2).Health);
        Assert.Equal(Side.Human, Game.Restore(hit.Snapshot).Winner);
        Assert.True(hit.Snapshot.Units.Single(u => u.Id == 1).HasAttacked);
        var count = game.Actions.Count; Assert.False(game.Attack(Side.Human, 1, 2).IsSuccess);
        Assert.Equal(count, game.Actions.Count);
    }

    [Fact]
    public void Ordered_computer_frames_are_restorable_and_finish_at_the_saved_state()
    {
        var game = Game.CreateRivergate(); game.EndTurn(Side.Human); new ComputerOpponent().PlayTurn(game);
        Assert.Contains(game.Actions, a => a.Side == Side.Computer && a.Kind == ActionKind.Move);
        Assert.All(game.Actions, action => Assert.NotNull(Game.Restore(action.Snapshot!)));
        Assert.Equal(JsonSerializer.Serialize(game.ExportSnapshot()), JsonSerializer.Serialize(game.Actions.Last().Snapshot));
        Assert.Equal(Side.Computer, game.Actions.First().Snapshot!.CurrentSide);
        Assert.Equal(Side.Human, game.Actions.Last().Snapshot!.CurrentSide);
    }
}
