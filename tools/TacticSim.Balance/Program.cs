using System.Diagnostics;
using TacticSim.Sim;
using TacticSim.Sim.Ai;

// AI-vs-AI balance runner. Each side plans from its own fog-filtered view, exactly like a real match.
//   dotnet run --project tools/TacticSim.Balance -- --games 500 --red Veteran --blue Veteran
string Arg(string name, string fallback)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
}

var games = int.Parse(Arg("--games", "200"));
var blueLevel = Enum.Parse<Difficulty>(Arg("--blue", "Veteran"), ignoreCase: true);
var redLevel = Enum.Parse<Difficulty>(Arg("--red", "Veteran"), ignoreCase: true);
var mapId = Arg("--map", "rivergate");
var firstSeed = int.Parse(Arg("--seed", "1"));
var content = GameContent.Load();
var map = content.Board(mapId).Map;

var results = new Dictionary<MatchResult, int> { [MatchResult.BlueWins] = 0, [MatchResult.RedWins] = 0, [MatchResult.Draw] = 0 };
var reasons = new Dictionary<string, int>();
var turnsTaken = new List<int>();
var vpCurve = new double[map.TurnLimit + 1, 2];
var held = map.Objectives.ToDictionary(o => o.Id, _ => new int[3]); // blue, red, nobody
var unitsFielded = new Dictionary<string, int>();
var unitsLost = new Dictionary<string, int>();
var orderUse = new Dictionary<string, Dictionary<OrderType, int>>();
var invalid = 0;
var planTimes = new List<double>();
var clock = new Stopwatch();

Order[] Plan(MatchState state, Side side, Difficulty level, int seed)
{
    clock.Restart();
    var orders = AiPlanner.Plan(content, Fog.StateFor(content, state, side), level, seed);
    planTimes.Add(clock.Elapsed.TotalMilliseconds);
    var check = OrderRules.Validate(content, state, side, orders);
    if (check.Ok) return check.Orders;
    invalid++;
    return OrderRules.Validate(content, state, side, []).Orders;
}

for (var game = 0; game < games; game++)
{
    var seed = firstSeed + game;
    var state = Scenario.Create(content, mapId, seed);
    while (!state.IsOver)
    {
        var blue = Plan(state, Side.Blue, blueLevel, seed * 2 + 1);
        var red = Plan(state, Side.Red, redLevel, seed * 2 + 2);
        foreach (var order in blue.Concat(red))
        {
            var type = state.Unit(order.UnitId)!.Type;
            if (!orderUse.TryGetValue(type, out var use)) orderUse[type] = use = [];
            use[order.Type] = use.GetValueOrDefault(order.Type) + 1;
        }
        state = TurnResolver.Resolve(content, state, blue, red).FinalState;
        foreach (var (id, side) in state.Control) held[id][side switch { Side.Blue => 0, Side.Red => 1, _ => 2 }]++;
    }
    results[state.Result!.Value]++;
    reasons[state.EndReason!] = reasons.GetValueOrDefault(state.EndReason!) + 1;
    turnsTaken.Add(state.VpHistory.Count);
    for (var turn = 1; turn <= map.TurnLimit; turn++)
    {
        var point = state.VpHistory.LastOrDefault(p => p.Turn <= turn) ?? new VpPoint(turn, 0, 0);
        vpCurve[turn, 0] += point.Blue;
        vpCurve[turn, 1] += point.Red;
    }
    foreach (var unit in state.Units)
    {
        unitsFielded[unit.Type] = unitsFielded.GetValueOrDefault(unit.Type) + 1;
        if (!unit.Alive) unitsLost[unit.Type] = unitsLost.GetValueOrDefault(unit.Type) + 1;
    }
}

string Pct(double part, double whole) => whole == 0 ? "-" : $"{100 * part / whole,5:0.0}%";
var totalTurns = turnsTaken.Sum();
Console.WriteLine($"TacticSim balance: {games} games on {map.Name}, Blue {blueLevel} vs Red {redLevel}, rules {content.RulesVersion}");
Console.WriteLine($"Blue wins {Pct(results[MatchResult.BlueWins], games)} | Red wins {Pct(results[MatchResult.RedWins], games)} | Draws {Pct(results[MatchResult.Draw], games)}");
var decisive = results[MatchResult.BlueWins] + results[MatchResult.RedWins];
Console.WriteLine($"Blue share of decisive games: {Pct(results[MatchResult.BlueWins], decisive)}");
Console.WriteLine($"Average turns {turnsTaken.Average():0.0} (min {turnsTaken.Min()}, max {turnsTaken.Max()}) | endings: " +
    string.Join(", ", reasons.OrderBy(r => r.Key).Select(r => $"{r.Key} {Pct(r.Value, games)}")));
Console.WriteLine("VP curve (avg Blue-Red by turn): " + string.Join(" ", Enumerable.Range(1, map.TurnLimit)
    .Select(t => $"{t}:{vpCurve[t, 0] / games:0.0}-{vpCurve[t, 1] / games:0.0}")));
Console.WriteLine("Objective held (Blue / Red / nobody, share of turns):");
foreach (var (id, counts) in held)
    Console.WriteLine($"  {id} {map.Objectives.First(o => o.Id == id).Label,-14} {Pct(counts[0], totalTurns)} {Pct(counts[1], totalTurns)} {Pct(counts[2], totalTurns)}");
Console.WriteLine("Units (lost / fielded) and order use:");
foreach (var type in unitsFielded.Keys.Order())
{
    var use = orderUse.GetValueOrDefault(type) ?? [];
    var total = use.Values.Sum();
    Console.WriteLine($"  {content.Units[type].Name,-15} lost {Pct(unitsLost.GetValueOrDefault(type), unitsFielded[type])} | " +
        string.Join(" ", Enum.GetValues<OrderType>().Select(o => $"{o} {Pct(use.GetValueOrDefault(o), total)}")));
}
planTimes.Sort();
Console.WriteLine($"AI plan time: median {planTimes[planTimes.Count / 2]:0.0} ms, p95 {planTimes[(int)(planTimes.Count * 0.95)]:0.0} ms, max {planTimes[^1]:0.0} ms");
Console.WriteLine($"Rejected AI order sets: {invalid}");
