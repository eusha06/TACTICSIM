using System.Text.Json;
using System.Text.Json.Serialization;

namespace TacticSim.Sim;

public enum Side { Blue, Red }
public enum MatchResult { BlueWins, RedWins, Draw }

public static class SideExtensions
{
    public static Side Enemy(this Side side) => side == Side.Blue ? Side.Red : Side.Blue;
}

public sealed class UnitState
{
    public int Id { get; set; }
    public Side Side { get; set; }
    public string Type { get; set; } = "";
    public Hex Pos { get; set; }
    public int Hp { get; set; }
    [JsonIgnore] public bool Alive => Hp > 0;
}

/// <summary>Where a side last saw an enemy that has since dropped out of sight.</summary>
public sealed record LastSeenMarker(int UnitId, string Type, Hex Pos, int Hp, int Turn);

public sealed record VpPoint(int Turn, int Blue, int Red);

/// <summary>The whole saved match. Pure data: the rules live in the resolver.</summary>
public sealed class MatchState
{
    public string Scenario { get; set; } = "";
    public string RulesVersion { get; set; } = "";
    /// <summary>The turn being planned (1-based). After the last turn it stays on that turn.</summary>
    public int Turn { get; set; } = 1;
    public int VpBlue { get; set; }
    public int VpRed { get; set; }
    public List<UnitState> Units { get; set; } = [];
    /// <summary>Objective id to controlling side (null = nobody or contested).</summary>
    public Dictionary<string, Side?> Control { get; set; } = [];
    public MatchResult? Result { get; set; }
    public string? EndReason { get; set; }
    public List<LastSeenMarker> BlueLastSeen { get; set; } = [];
    public List<LastSeenMarker> RedLastSeen { get; set; } = [];
    public List<VpPoint> VpHistory { get; set; } = [];
    /// <summary>Enemy units each side destroyed while it could see them.</summary>
    public int BlueKills { get; set; }
    public int RedKills { get; set; }
    public int AiSeed { get; set; }

    [JsonIgnore] public bool IsOver => Result is not null;

    public List<LastSeenMarker> LastSeen(Side side) => side == Side.Blue ? BlueLastSeen : RedLastSeen;
    public int Vp(Side side) => side == Side.Blue ? VpBlue : VpRed;
    public UnitState? Unit(int id) => Units.FirstOrDefault(u => u.Id == id);

    public MatchState Clone() => JsonSerializer.Deserialize<MatchState>(JsonSerializer.Serialize(this, SimJson.Options), SimJson.Options)!;
}

public static class SimJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        return options;
    }
}

public static class Scenario
{
    /// <summary>A fresh match from a map's deployments. Blue units get the lowest ids.</summary>
    public static MatchState Create(GameContent content, string mapId, int aiSeed = 1)
    {
        var map = content.Board(mapId).Map;
        var state = new MatchState { Scenario = map.Id, RulesVersion = content.RulesVersion, AiSeed = aiSeed };
        var id = 1;
        foreach (var (side, list) in new[] { (Side.Blue, map.Deployments.Blue), (Side.Red, map.Deployments.Red) })
            foreach (var d in list)
                state.Units.Add(new UnitState
                {
                    Id = id++, Side = side, Type = d.Unit, Pos = new Hex(d.Col, d.Row), Hp = content.Units[d.Unit].Hp
                });
        foreach (var o in map.Objectives) state.Control[o.Id] = null;
        return state;
    }
}
