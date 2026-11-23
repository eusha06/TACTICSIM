using System.Text.Json;
using static TacticSim.Sim.Tests.World;

namespace TacticSim.Sim.Tests;

/// <summary>
/// Writes client/test/vectors/*.json: cases the TypeScript preview rules must reproduce exactly.
/// Regenerated on every test run; the output is deterministic, so Git only sees real rule changes.
/// </summary>
public class GoldenVectors
{
    private static readonly GameContent C = V1;
    private static readonly string Dir = System.IO.Path.Combine(RepoRoot, "client", "test", "vectors");

    private static void Write(string name, object value)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(System.IO.Path.Combine(Dir, name), JsonSerializer.Serialize(value, SimJson.Options) + "\n");
    }

    private static readonly Hex[] Observers =
        [new(0, 0), new(1, 4), new(3, 6), new(4, 6), new(5, 3), new(7, 2), new(8, 5), new(10, 5), new(11, 7), new(13, 9), new(14, 10), new(6, 9)];

    [Fact]
    public void Emit_hex_and_los_vectors()
    {
        var board = C.Board("rivergate");
        var hex = new List<object>();
        var los = new List<object>();
        foreach (var from in Observers)
            foreach (var to in board.AllHexes().Where(h => HexMath.Distance(from, h) <= 6))
            {
                hex.Add(new { a = from, b = to, distance = HexMath.Distance(from, to), line = HexMath.Line(from, to) });
                los.Add(new { map = "rivergate", from, to, blocker = Vision.Blocker(board, from, to) });
            }
        var vision = C.Units.Keys.Order().SelectMany(type => Observers.Select(at =>
        {
            var unit = new UnitState { Type = type, Pos = at, Hp = 1 };
            return new { map = "rivergate", unit = type, at, visible = Vision.VisibleHexes(C, board, [unit]).Select(board.Index).Order().ToArray() };
        })).ToArray();
        Write("hex.json", hex);
        Write("los.json", new { los, vision });
        Assert.Contains(los, l => l.GetType().GetProperty("blocker")!.GetValue(l) is not null);
    }

    [Fact]
    public void Emit_path_vectors()
    {
        var cases = new List<object>();
        foreach (var map in new[] { "rivergate", "bootcamp" })
        {
            var board = C.Board(map);
            var start = Scenario.Create(C, map);
            foreach (var unit in start.Units)
            {
                var blocked = start.Units.Where(u => u.Side != unit.Side).Take(2).Select(u => u.Pos).ToHashSet();
                var reach = Pathfinder.Reachable(C, board, C.Units[unit.Type], unit.Pos, blocked);
                cases.Add(new
                {
                    map, unit = unit.Type, start = unit.Pos, blocked = blocked.OrderBy(board.Index).ToArray(),
                    reachable = reach.Values.OrderBy(p => board.Index(p.Dest)).ToArray()
                });
            }
        }
        Write("paths.json", cases);
        Assert.Contains(cases, c => ((PathInfo[])c.GetType().GetProperty("reachable")!.GetValue(c)!).Any(p => p.RoadMarch));
    }

    [Fact]
    public void Emit_damage_vectors()
    {
        var board = C.Board("rivergate");
        var spots = C.Terrain.Values.Where(t => t.Passable)
            .Select(t => board.AllHexes().First(h => board.TerrainAt(h).Id == t.Id)).ToArray();
        var cases = new List<object>();
        foreach (var attacker in C.Units.Keys.Order())
            foreach (var target in C.Units.Keys.Order())
                foreach (var at in spots)
                    foreach (var holding in new[] { false, true })
                    {
                        var unit = new UnitState { Type = target, Pos = at, Hp = 1 };
                        cases.Add(new
                        {
                            attacker, target, terrain = board.TerrainAt(at).Id, holding,
                            cover = Combat.Cover(C, board, unit, holding),
                            damage = Combat.Damage(C, C.Units[attacker], C.Units[target], Combat.Cover(C, board, unit, holding))
                        });
                    }
        Write("damage.json", cases);
    }

    [Fact]
    public void Emit_a_full_resolved_turn()
    {
        var start = Scenario.Create(C, "rivergate");
        Order[] blue = [Move(4, (1, 2), (2, 2), (3, 2), (4, 2), (5, 2), (6, 2), (7, 2)), Move(6, (1, 8), (2, 8), (3, 8), (4, 8), (5, 8), (6, 8), (7, 8))];
        Order[] red = [Move(10, (13, 2), (12, 2), (11, 2), (10, 2), (9, 2), (8, 2)), Move(12, (13, 8), (12, 8), (11, 8), (10, 8), (9, 8), (8, 8))];
        var result = TurnResolver.Resolve(C, start, blue, red);
        Write("turn.json", new { start, blue, red, result, blueView = Fog.ReplayFor(C, result, Side.Blue) });
        Assert.Contains(result.Events(), x => x.Event.Kind == EventKind.Shot);
    }
}
