using System.Text.Json;

namespace TacticSim.Sim.Tests;

/// <summary>Builds tiny test battlefields from the real v1 rules with a custom map.</summary>
public static class World
{
    public static string RepoRoot { get; } = FindRoot();
    public static GameContent V1 => GameContent.Load();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(System.IO.Path.Combine(dir.FullName, "TacticSim.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    public static string ContentFile(string name) => File.ReadAllText(System.IO.Path.Combine(RepoRoot, "content", "v1", name));

    /// <summary>v1 rules with one test map. Objectives default to the bottom-right hex.</summary>
    public static GameContent With(string[] rows, params (string Id, int Col, int Row)[] objectives) =>
        GameContent.FromJson(ContentFile("rules.json"), ContentFile("terrain.json"), ContentFile("units.json"),
            [MapJson(rows, objectives)]);

    public static string MapJson(string[] rows, (string Id, int Col, int Row)[] objectives, int turnLimit = 12, int vpToWin = 12)
    {
        var open = new List<(int, int)>();
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++)
                if (rows[r][c] != '~') open.Add((c, r));
        if (objectives.Length == 0) objectives = [("O", open[^1].Item1, open[^1].Item2)];
        return JsonSerializer.Serialize(new
        {
            id = "test", name = "Test", width = rows[0].Length, height = rows.Length, tiles = rows,
            objectives = objectives.Select(o => new { id = o.Id, label = "Objective " + o.Id, col = o.Col, row = o.Row }),
            deployments = new
            {
                blue = new[] { new { unit = "rifle", col = open[0].Item1, row = open[0].Item2 } },
                red = new[] { new { unit = "rifle", col = open[1].Item1, row = open[1].Item2 } }
            },
            turnLimit, vpToWin
        });
    }

    public static string[] Open(int cols = 9, int rows = 5) => Enumerable.Repeat(new string('.', cols), rows).ToArray();

    public static (Side, string, int, int) Blue(string type, int col, int row) => (Side.Blue, type, col, row);
    public static (Side, string, int, int) Red(string type, int col, int row) => (Side.Red, type, col, row);

    /// <summary>A match state on the content's first map with these units, ids 1..n in the order given.</summary>
    public static MatchState State(GameContent content, params (Side Side, string Type, int Col, int Row)[] units)
    {
        var map = content.Maps.Values.First();
        var state = new MatchState { Scenario = map.Id, RulesVersion = content.RulesVersion };
        var id = 1;
        foreach (var (side, type, col, row) in units)
            state.Units.Add(new UnitState { Id = id++, Side = side, Type = type, Pos = new Hex(col, row), Hp = content.Units[type].Hp });
        foreach (var o in map.Objectives) state.Control[o.Id] = null;
        return state;
    }

    public static TurnResult Resolve(GameContent content, MatchState state, params Order[] orders) =>
        TurnResolver.Resolve(content, state,
            orders.Where(o => state.Unit(o.UnitId)!.Side == Side.Blue).ToArray(),
            orders.Where(o => state.Unit(o.UnitId)!.Side == Side.Red).ToArray());

    public static Hex[] Path(params (int Col, int Row)[] hexes) => hexes.Select(h => new Hex(h.Col, h.Row)).ToArray();
    public static Order Move(int id, params (int, int)[] path) => new(id, OrderType.Move, Path(path));

    public static IEnumerable<(int Tick, SimEvent Event)> Events(this TurnResult result) =>
        result.Ticks.SelectMany(t => t.Events.Select(e => (t.Tick, e)));

    public static IEnumerable<(int Tick, SimEvent Event)> Of(this TurnResult result, string kind) =>
        result.Events().Where(x => x.Event.Kind == kind);

    public static UnitState U(this TurnResult result, int id) => result.FinalState.Unit(id)!;
}
