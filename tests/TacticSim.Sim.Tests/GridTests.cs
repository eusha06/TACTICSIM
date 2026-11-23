using static TacticSim.Sim.Tests.World;

namespace TacticSim.Sim.Tests;

public class HexTests
{
    [Fact]
    public void Offset_and_cube_round_trip_and_distance_is_symmetric()
    {
        for (var row = -2; row < 12; row++)
            for (var col = -2; col < 16; col++)
            {
                var hex = new Hex(col, row);
                var (x, y, z) = HexMath.ToCube(hex);
                Assert.Equal(0, x + y + z);
                Assert.Equal(hex, HexMath.FromCube(x, z));
                Assert.Equal(HexMath.Distance(hex, new Hex(3, 4)), HexMath.Distance(new Hex(3, 4), hex));
            }
    }

    [Fact]
    public void Odd_rows_are_shifted_right()
    {
        // Even row 2: diagonal neighbors sit at col-1 and col. Odd row 3: at col and col+1.
        Assert.Equal([new Hex(4, 2), new Hex(3, 1), new Hex(2, 1), new Hex(2, 2), new Hex(2, 3), new Hex(3, 3)],
            HexMath.Neighbors(new Hex(3, 2)));
        Assert.True(HexMath.Adjacent(new Hex(3, 3), new Hex(4, 2)));
        Assert.False(HexMath.Adjacent(new Hex(3, 2), new Hex(4, 1)));
        Assert.Equal(1, HexMath.Distance(new Hex(3, 3), new Hex(4, 4)));
        Assert.Equal(7, HexMath.Distance(new Hex(0, 2), new Hex(7, 2)));
    }

    [Fact]
    public void Lines_are_continuous_include_both_ends_and_are_stable()
    {
        var a = new Hex(1, 1);
        foreach (var b in new[] { new Hex(9, 6), new Hex(1, 7), new Hex(6, 1), new Hex(1, 1), new Hex(0, 9) })
        {
            var line = HexMath.Line(a, b);
            Assert.Equal(HexMath.Distance(a, b) + 1, line.Count);
            Assert.Equal(a, line[0]);
            Assert.Equal(b, line[^1]);
            for (var i = 1; i < line.Count; i++) Assert.True(HexMath.Adjacent(line[i - 1], line[i]));
            Assert.Equal(line, HexMath.Line(a, b));
        }
    }

    [Fact]
    public void Labels_use_column_letters_and_one_based_rows() => Assert.Equal("H5", new Hex(7, 4).Label);
}

public class ContentTests
{
    [Fact]
    public void V1_content_matches_the_spec_tables()
    {
        var c = V1;
        Assert.Equal("v1", c.RulesVersion);
        (char Code, int? Foot, int? Vehicle, int Cover, bool Blocks)[] table =
        [
            ('.', 1, 1, 0, false), ('=', 1, 1, 0, false), ('F', 2, 3, 2, true), ('H', 2, 2, 1, true),
            ('T', 1, 2, 3, true), ('~', null, null, 0, false), ('B', 1, 1, 0, false)
        ];
        foreach (var row in table)
        {
            var t = c.Terrain[row.Code];
            Assert.Equal((row.Foot, row.Vehicle, row.Cover, row.Blocks), (t.FootCost, t.VehicleCost, t.Cover, t.BlocksLos));
        }
        Assert.Equal(["armor", "artillery", "at", "recon", "rifle"], c.Units.Keys.Order());
        var armor = c.Units["armor"];
        Assert.Equal((18, 5, 1, 3, 6, 5, 3), (armor.Hp, armor.Move, armor.MinRange, armor.MaxRange, armor.AtkFoot, armor.AtkVehicle, armor.Vision));
        var map = c.Board("rivergate").Map;
        Assert.Equal((15, 11, 12, 12), (map.Width, map.Height, map.TurnLimit, map.VpToWin));
        Assert.Equal(["A", "B", "C"], map.Objectives.Select(o => o.Id));
        Assert.Equal(6, map.Deployments.Blue.Length);
        Assert.Equal('B', map.Tiles[2][7]);
        Assert.Equal((9, 7, 1), (c.Board("bootcamp").Width, c.Board("bootcamp").Height, c.Board("bootcamp").Map.Objectives.Length));
    }

    [Fact]
    public void Rivergate_river_is_crossable_only_at_the_two_bridges()
    {
        var board = V1.Board("rivergate");
        var crossings = Enumerable.Range(0, board.Height).Where(r => board.TerrainAt(new Hex(7, r)).Passable).ToArray();
        Assert.Equal([2, 8], crossings);
    }

    public static TheoryData<string, string> BrokenMaps => new()
    {
        { """{"id":"m","name":"m","width":3,"height":1,"tiles":[".."],"objectives":[{"id":"O","label":"O","col":0,"row":0}],"deployments":{"blue":[{"unit":"rifle","col":0,"row":0}],"red":[{"unit":"rifle","col":1,"row":0}]},"turnLimit":5,"vpToWin":5}""", "row 1 needs 3 tiles" },
        { """{"id":"m","name":"m","width":2,"height":1,"tiles":[".?"],"objectives":[{"id":"O","label":"O","col":0,"row":0}],"deployments":{"blue":[{"unit":"rifle","col":0,"row":0}],"red":[{"unit":"rifle","col":1,"row":0}]},"turnLimit":5,"vpToWin":5}""", "unknown tile code '?'" },
        { """{"id":"m","name":"m","width":2,"height":1,"tiles":[".~"],"objectives":[{"id":"O","label":"O","col":1,"row":0}],"deployments":{"blue":[{"unit":"rifle","col":0,"row":0}],"red":[{"unit":"rifle","col":0,"row":0}]},"turnLimit":5,"vpToWin":5}""", "objective O must be on passable ground" },
        { """{"id":"m","name":"m","width":2,"height":1,"tiles":[".."],"objectives":[{"id":"O","label":"O","col":1,"row":0}],"deployments":{"blue":[{"unit":"tank","col":0,"row":0}],"red":[{"unit":"rifle","col":1,"row":0}]},"turnLimit":5,"vpToWin":5}""", "unknown unit 'tank'" },
        { """{"id":"m","name":"m","width":2,"height":1,"tiles":[".."],"objectives":[{"id":"O","label":"O","col":1,"row":0}],"deployments":{"blue":[{"unit":"rifle","col":0,"row":0}],"red":[{"unit":"rifle","col":0,"row":0}]},"turnLimit":5,"vpToWin":5}""", "two units deploy" },
        { "{not json", "map:" },
    };

    [Theory, MemberData(nameof(BrokenMaps))]
    public void Broken_maps_are_rejected_with_a_named_problem(string json, string expected)
    {
        var error = Assert.Throws<InvalidDataException>(() => GameContent.FromJson(ContentFile("rules.json"),
            ContentFile("terrain.json"), ContentFile("units.json"), [json]));
        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public void Broken_units_and_rules_are_rejected()
    {
        var units = ContentFile("units.json").Replace("\"minRange\": 3, \"maxRange\": 6", "\"minRange\": 7, \"maxRange\": 6");
        Assert.Contains("minRange", Assert.Throws<InvalidDataException>(() => GameContent.FromJson(ContentFile("rules.json"),
            ContentFile("terrain.json"), units, [])).Message);
        var rules = ContentFile("rules.json").Replace("\"barrageTick\": 6", "\"barrageTick\": 60");
        Assert.Contains("barrageTick", Assert.Throws<InvalidDataException>(() => GameContent.FromJson(rules,
            ContentFile("terrain.json"), ContentFile("units.json"), [])).Message);
        var terrain = ContentFile("terrain.json").Replace("\"code\": \"F\"", "\"code\": \".\"");
        Assert.Contains("duplicate terrain code", Assert.Throws<InvalidDataException>(() => GameContent.FromJson(
            ContentFile("rules.json"), terrain, ContentFile("units.json"), [])).Message);
        Assert.Throws<ArgumentException>(() => V1.Board("nowhere"));
    }
}

public class VisionTests
{
    [Theory]
    [InlineData(".....", 4, true)]   // open
    [InlineData("..F..", 4, false)]  // forest between blocks
    [InlineData("..F..", 2, true)]   // you can see into the forest hex itself
    [InlineData("..T..", 4, false)]  // town blocks
    [InlineData("..H..", 4, false)]  // hill between two lower hexes blocks
    [InlineData("H.F..", 4, true)]   // a unit on a hill sees over forest
    [InlineData("..F.H", 4, true)]   // and a hill-top is seen over forest (sight is two-way)
    [InlineData("..~..", 4, true)]   // river does not block sight
    public void Line_of_sight_follows_terrain(string row, int toCol, bool clear)
    {
        var board = With([row]).Board("test");
        Assert.Equal(clear, Vision.HasLos(board, new Hex(0, 0), new Hex(toCol, 0)));
        Assert.Equal(clear, Vision.HasLos(board, new Hex(toCol, 0), new Hex(0, 0)));
    }

    [Fact]
    public void Blocker_names_the_first_blocking_hex()
    {
        var board = With(["..FT."]).Board("test");
        Assert.Equal(new Hex(2, 0), Vision.Blocker(board, new Hex(0, 0), new Hex(4, 0)));
        Assert.Null(Vision.Blocker(board, new Hex(0, 0), new Hex(1, 0)));
    }

    [Fact]
    public void Vision_radius_grows_by_one_on_a_hill()
    {
        var c = With(["........", "H......."]);
        var board = c.Board("test");
        var low = new UnitState { Type = "rifle", Pos = new Hex(0, 0), Hp = 10 };
        var high = new UnitState { Type = "rifle", Pos = new Hex(0, 1), Hp = 10 };
        Assert.True(Vision.CanSee(c, board, low, new Hex(3, 0)));
        Assert.False(Vision.CanSee(c, board, low, new Hex(4, 0)));
        Assert.Equal(4, Vision.VisionRadius(c, board, high));
        Assert.True(Vision.CanSee(c, board, high, new Hex(4, 1)));
    }

    [Fact]
    public void Side_view_lists_only_enemies_on_visible_hexes()
    {
        var c = With(["....F...."]);
        var s = State(c, Blue("rifle", 0, 0), Red("rifle", 2, 0), Red("rifle", 6, 0));
        var view = Vision.ViewOf(c, s, Side.Blue);
        Assert.Equal([2], view.VisibleEnemies.Select(u => u.Id));
        Assert.DoesNotContain(new Hex(5, 0), view.VisibleHexes);
    }
}

public class PathfinderTests
{
    private static Dictionary<Hex, PathInfo> Reach(GameContent c, string type, Hex from, params Hex[] blocked) =>
        Pathfinder.Reachable(c, c.Board("test"), c.Units[type], from, blocked.ToHashSet());

    [Theory]
    [InlineData('.', 1, 1)]
    [InlineData('=', 1, 1)]
    [InlineData('F', 2, 3)]
    [InlineData('H', 2, 2)]
    [InlineData('T', 1, 2)]
    [InlineData('B', 1, 1)]
    public void Every_terrain_costs_its_table_value(char code, int foot, int vehicle)
    {
        var c = With([$".{code}..."]);
        Assert.Equal(foot, Reach(c, "rifle", new Hex(0, 0))[new Hex(1, 0)].Cost);
        Assert.Equal(vehicle, Reach(c, "armor", new Hex(0, 0))[new Hex(1, 0)].Cost);
    }

    [Fact]
    public void River_is_impassable_and_blocked_hexes_are_avoided()
    {
        var c = With(["..~..", "..~..", "....."]);
        var reach = Reach(c, "rifle", new Hex(0, 0), new Hex(1, 1));
        Assert.DoesNotContain(new Hex(2, 0), reach.Keys);
        Assert.DoesNotContain(new Hex(1, 1), reach.Keys);
        Assert.DoesNotContain(reach.Values, p => p.Path.Contains(new Hex(1, 1)));
    }

    [Fact]
    public void Road_march_adds_two_ticks_only_for_vehicles_on_an_all_road_path()
    {
        var c = With(["=========", "........."]);
        var armor = Reach(c, "armor", new Hex(0, 0));
        Assert.False(armor[new Hex(5, 0)].RoadMarch);
        Assert.True(armor[new Hex(7, 0)].RoadMarch);
        Assert.Equal(7, armor[new Hex(7, 0)].Cost);
        Assert.DoesNotContain(new Hex(8, 0), armor.Keys);
        Assert.DoesNotContain(new Hex(6, 1), armor.Keys); // off the road: Move 5 only
        Assert.DoesNotContain(new Hex(5, 0), Reach(c, "rifle", new Hex(0, 0)).Keys);
        // Recon: Move 7 + 2 is capped by the 8-tick turn.
        Assert.Equal(8, Reach(c, "recon", new Hex(0, 0)).Values.Max(p => p.Cost));
    }

    [Fact]
    public void Routes_are_deterministic_and_shortest()
    {
        var c = With(World.Open(9, 7));
        var first = Reach(c, "recon", new Hex(4, 3));
        var second = Reach(c, "recon", new Hex(4, 3));
        Assert.All(first, kv => Assert.Equal(kv.Value.Path, second[kv.Key].Path));
        Assert.All(first, kv => Assert.Equal(HexMath.Distance(new Hex(4, 3), kv.Key), kv.Value.Cost));
    }

    [Fact]
    public void Path_check_explains_every_problem()
    {
        var c = With(["..~..", "....F"]);
        var board = c.Board("test");
        string? Check(string type, params Hex[] path) =>
            Pathfinder.Check(c, board, c.Units[type], new Hex(0, 0), path, new HashSet<Hex> { new(3, 1) }, out _, out _);
        Assert.Equal("Pick a destination hex: the path is empty.", Check("rifle"));
        Assert.Equal("The path jumps from A1 to C1. Each step must be to a neighboring hex.", Check("rifle", new Hex(2, 0)));
        Assert.Equal("River at C1 is impassable.", Check("rifle", new Hex(1, 0), new Hex(2, 0)));
        Assert.Equal("D2 is held by an enemy unit.", Check("rifle", new Hex(0, 1), new Hex(1, 1), new Hex(2, 1), new Hex(3, 1)));
        Assert.Equal("The path visits A1 twice.", Check("rifle", new Hex(1, 0), new Hex(0, 0)));
        Assert.Equal("A0 is off the map.", Check("rifle", new Hex(0, -1)));
        Assert.Equal("Not enough move: the path costs 4 ticks, Artillery has 3.",
            Check("artillery", new Hex(0, 1), new Hex(1, 1), new Hex(2, 1), new Hex(3, 0)));
        Assert.Null(Check("rifle", new Hex(1, 0)));
    }

    [Fact]
    public void Cost_field_gives_multi_turn_distance_to_a_target()
    {
        var c = With(["..F..", "....."]);
        var field = Pathfinder.CostTo(c.Board("test"), c.Units["rifle"], new Hex(4, 0));
        var board = c.Board("test");
        Assert.Equal(0, field[board.Index(new Hex(4, 0))]);
        Assert.Equal(1, field[board.Index(new Hex(3, 0))]);
        Assert.Equal(5, field[board.Index(new Hex(0, 0))]); // around or through the forest
        Assert.All(Pathfinder.CostTo(With(["..~.."]).Board("test"), c.Units["rifle"], new Hex(2, 0)), d => Assert.Equal(int.MaxValue, d));
    }
}

public class CombatTests
{
    [Fact]
    public void Damage_is_attack_minus_cover_with_a_minimum_of_one()
    {
        var c = With([".FT=."]);
        var board = c.Board("test");
        UnitState At(string type, int col) => new() { Type = type, Pos = new Hex(col, 0), Hp = c.Units[type].Hp };
        var armor = At("armor", 0);
        Assert.Equal(6, Combat.Damage(c, board, armor, At("rifle", 0), false));          // open
        Assert.Equal(5, Combat.Damage(c, board, armor, At("rifle", 0), true));           // hold +1
        Assert.Equal(3, Combat.Damage(c, board, armor, At("rifle", 1), false));          // forest 2 + rifle 1
        Assert.Equal(2, Combat.Damage(c, board, armor, At("rifle", 2), false));          // town 3 + rifle 1
        Assert.Equal(1, Combat.Damage(c, board, armor, At("rifle", 2), true));           // never below 1
        Assert.Equal(6, Combat.Damage(c, board, At("at", 0), At("armor", 1), false));     // 7 - (2 - 1)
        Assert.Equal(1, Combat.Cover(c, board, At("armor", 1), false));
        Assert.Equal(7, Combat.Damage(c, board, At("at", 0), At("armor", 3), false));     // road
        Assert.Equal(2, Combat.Damage(c, board, At("recon", 0), At("rifle", 4), false));
    }

    [Fact]
    public void Best_target_prefers_damage_then_low_hp_then_low_id()
    {
        var c = With(World.Open());
        var board = c.Board("test");
        var shooter = new UnitState { Id = 1, Type = "armor", Pos = new Hex(0, 0), Hp = 18 };
        var rifle = new UnitState { Id = 5, Type = "rifle", Pos = new Hex(1, 0), Hp = 10 };
        var recon = new UnitState { Id = 3, Type = "recon", Pos = new Hex(2, 0), Hp = 1 };
        var hurt = new UnitState { Id = 7, Type = "rifle", Pos = new Hex(3, 0), Hp = 4 };
        var twin = new UnitState { Id = 6, Type = "rifle", Pos = new Hex(3, 0), Hp = 4 };
        Assert.Same(rifle, Combat.BestTarget(c, board, shooter, [recon, rifle], _ => false));
        Assert.Same(hurt, Combat.BestTarget(c, board, shooter, [rifle, hurt], _ => false));
        Assert.Same(twin, Combat.BestTarget(c, board, shooter, [hurt, twin], _ => false));
        Assert.Null(Combat.BestTarget(c, board, shooter, [], _ => false));
    }
}
