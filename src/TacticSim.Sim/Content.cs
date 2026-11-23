using System.Collections.Concurrent;
using System.Text.Json;

namespace TacticSim.Sim;

public sealed record RulesDef(string RulesVersion, int TicksPerTurn, int HoldCover, int RoadMarchBonus,
    int BarrageTick, int HillVisionBonus, int MinDamage);

public sealed record TerrainDef(string Code, string Id, string Name, int? FootCost, int? VehicleCost,
    int Cover, bool BlocksLos, bool Road = false, bool Hill = false)
{
    public bool Passable => FootCost is not null || VehicleCost is not null;
}

public sealed record UnitDef(string Id, string Name, string Short, string Class, int Hp, int Move,
    int MinRange, int MaxRange, int AtkFoot, int AtkVehicle, int Vision, bool Captures,
    bool Indirect = false, bool CannotMoveAndFire = false, Dictionary<string, int>? CoverMods = null,
    string Special = "")
{
    public bool IsFoot => Class == "foot";
    public bool IsVehicle => Class == "vehicle";
    public int AttackVs(UnitDef target) => target.IsFoot ? AtkFoot : AtkVehicle;
    public int CoverModIn(TerrainDef terrain) => CoverMods?.GetValueOrDefault(terrain.Id) ?? 0;
}

public sealed record ObjectiveDef(string Id, string Label, int Col, int Row)
{
    public Hex Hex => new(Col, Row);
}

public sealed record DeploymentDef(string Unit, int Col, int Row);
public sealed record DeploymentsDef(DeploymentDef[] Blue, DeploymentDef[] Red);

public sealed record MapDef(string Id, string Name, int Width, int Height, string[] Tiles,
    ObjectiveDef[] Objectives, DeploymentsDef Deployments, int TurnLimit, int VpToWin, string? RedScript = null);

/// <summary>One map plus the terrain table: everything spatial the rules ask about.</summary>
public sealed class Board(MapDef map, IReadOnlyDictionary<char, TerrainDef> terrain)
{
    public MapDef Map { get; } = map;
    public int Width => Map.Width;
    public int Height => Map.Height;

    public bool InBounds(Hex h) => h.Col >= 0 && h.Col < Width && h.Row >= 0 && h.Row < Height;
    public TerrainDef TerrainAt(Hex h) => terrain[Map.Tiles[h.Row][h.Col]];
    public int Index(Hex h) => h.Row * Width + h.Col;
    public Hex FromIndex(int index) => new(index % Width, index / Width);

    /// <summary>Ticks to enter the hex, or null when the unit can't enter it.</summary>
    public int? MoveCost(Hex h, UnitDef unit)
    {
        if (!InBounds(h)) return null;
        var t = TerrainAt(h);
        return unit.IsFoot ? t.FootCost : t.VehicleCost;
    }

    public IEnumerable<Hex> AllHexes()
    {
        for (var row = 0; row < Height; row++)
            for (var col = 0; col < Width; col++)
                yield return new Hex(col, row);
    }

    public ObjectiveDef? ObjectiveAt(Hex h) => Map.Objectives.FirstOrDefault(o => o.Hex == h);
}

/// <summary>Versioned rules content (content/{version}/*.json), loaded and validated once.</summary>
public sealed class GameContent
{
    private static readonly ConcurrentDictionary<string, GameContent> Cache = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, Board> _boards = [];

    public RulesDef Rules { get; }
    public IReadOnlyDictionary<char, TerrainDef> Terrain { get; }
    public IReadOnlyDictionary<string, UnitDef> Units { get; }
    public IReadOnlyDictionary<string, MapDef> Maps { get; }
    public string RulesVersion => Rules.RulesVersion;

    private GameContent(RulesDef rules, TerrainDef[] terrain, UnitDef[] units, MapDef[] maps)
    {
        Rules = rules;
        Terrain = terrain.ToDictionary(t => t.Code[0]);
        Units = units.ToDictionary(u => u.Id);
        Maps = maps.ToDictionary(m => m.Id);
        foreach (var map in maps) _boards[map.Id] = new Board(map, Terrain);
    }

    public Board Board(string mapId) => _boards.TryGetValue(mapId, out var board)
        ? board : throw new ArgumentException($"Unknown map '{mapId}'.");

    public static GameContent Load(string version = "v1") => Cache.GetOrAdd(version, v =>
    {
        var assembly = typeof(GameContent).Assembly;
        string Read(string name)
        {
            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidDataException($"Missing content file {name}.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        var prefix = $"content/{v}/";
        var mapNames = assembly.GetManifestResourceNames()
            .Select(n => n.Replace('\\', '/'))
            .Where(n => n.StartsWith(prefix + "maps/")).Order().ToArray();
        // Resource names keep the build machine's separator; look each up by its original name.
        var original = assembly.GetManifestResourceNames().ToDictionary(n => n.Replace('\\', '/'));
        return FromJson(Read(original[prefix + "rules.json"]), Read(original[prefix + "terrain.json"]),
            Read(original[prefix + "units.json"]), mapNames.Select(n => Read(original[n])));
    });

    /// <summary>Parses and validates content. Every problem names the file and the field.</summary>
    public static GameContent FromJson(string rulesJson, string terrainJson, string unitsJson, IEnumerable<string> mapJsons)
    {
        var rules = Parse<RulesDef>(rulesJson, "rules.json");
        Require(!string.IsNullOrWhiteSpace(rules.RulesVersion), "rules.json: rulesVersion is required.");
        Require(rules.TicksPerTurn is > 0 and <= 20, "rules.json: ticksPerTurn must be 1-20.");
        Require(rules.BarrageTick >= 1 && rules.BarrageTick <= rules.TicksPerTurn, "rules.json: barrageTick must be within the turn.");
        Require(rules.MinDamage >= 1, "rules.json: minDamage must be at least 1.");

        var terrain = Parse<TerrainFile>(terrainJson, "terrain.json").Terrain ?? [];
        Require(terrain.Length > 0, "terrain.json: needs at least one terrain.");
        foreach (var t in terrain)
        {
            Require(t.Code is { Length: 1 }, $"terrain.json: '{t.Id}' code must be one character.");
            Require(t.FootCost is null or > 0 && t.VehicleCost is null or > 0, $"terrain.json: '{t.Id}' costs must be positive.");
            Require(t.Cover >= 0, $"terrain.json: '{t.Id}' cover can't be negative.");
        }
        Require(terrain.Select(t => t.Code).Distinct().Count() == terrain.Length, "terrain.json: duplicate terrain code.");

        var units = Parse<UnitFile>(unitsJson, "units.json").Units ?? [];
        Require(units.Length > 0, "units.json: needs at least one unit.");
        foreach (var u in units)
        {
            Require(u.Class is "foot" or "vehicle", $"units.json: '{u.Id}' class must be foot or vehicle.");
            Require(u.Hp > 0 && u.Move > 0 && u.Vision > 0, $"units.json: '{u.Id}' hp, move and vision must be positive.");
            Require(u.MinRange >= 1 && u.MinRange <= u.MaxRange, $"units.json: '{u.Id}' needs 1 <= minRange <= maxRange.");
            Require(u.AtkFoot >= 0 && u.AtkVehicle >= 0, $"units.json: '{u.Id}' attack can't be negative.");
            foreach (var mod in u.CoverMods ?? [])
                Require(terrain.Any(t => t.Id == mod.Key), $"units.json: '{u.Id}' coverMods names unknown terrain '{mod.Key}'.");
        }
        Require(units.Select(u => u.Id).Distinct().Count() == units.Length, "units.json: duplicate unit id.");

        var maps = mapJsons.Select(json => Parse<MapDef>(json, "map")).ToArray();
        foreach (var m in maps) ValidateMap(m, terrain, units);
        Require(maps.Select(m => m.Id).Distinct().Count() == maps.Length, "maps: duplicate map id.");
        return new GameContent(rules, terrain, units, maps);
    }

    private static void ValidateMap(MapDef m, TerrainDef[] terrain, UnitDef[] units)
    {
        var file = $"maps/{m.Id}.json";
        Require(m.Width > 0 && m.Width <= 26 && m.Height > 0, $"{file}: width must be 1-26 and height positive.");
        Require(m.Tiles?.Length == m.Height, $"{file}: needs exactly {m.Height} tile rows.");
        for (var row = 0; row < m.Height; row++)
        {
            Require(m.Tiles![row].Length == m.Width, $"{file}: row {row + 1} needs {m.Width} tiles.");
            foreach (var code in m.Tiles[row])
                Require(terrain.Any(t => t.Code[0] == code), $"{file}: unknown tile code '{code}' in row {row + 1}.");
        }
        TerrainDef At(int col, int row) => terrain.First(t => t.Code[0] == m.Tiles![row][col]);
        bool Inside(int col, int row) => col >= 0 && col < m.Width && row >= 0 && row < m.Height;
        Require(m.Objectives is { Length: > 0 }, $"{file}: needs at least one objective.");
        foreach (var o in m.Objectives!)
            Require(Inside(o.Col, o.Row) && At(o.Col, o.Row).Passable, $"{file}: objective {o.Id} must be on passable ground.");
        var spots = new HashSet<(int, int)>();
        foreach (var d in (m.Deployments?.Blue ?? []).Concat(m.Deployments?.Red ?? []))
        {
            Require(units.Any(u => u.Id == d.Unit), $"{file}: deployment names unknown unit '{d.Unit}'.");
            Require(Inside(d.Col, d.Row) && At(d.Col, d.Row).Passable, $"{file}: deployment at {d.Col},{d.Row} must be passable.");
            Require(spots.Add((d.Col, d.Row)), $"{file}: two units deploy at {d.Col},{d.Row}.");
        }
        Require(m.Deployments is { Blue.Length: > 0, Red.Length: > 0 }, $"{file}: both sides need units.");
        Require(m.TurnLimit > 0 && m.VpToWin > 0, $"{file}: turnLimit and vpToWin must be positive.");
    }

    private static T Parse<T>(string json, string file)
    {
        try { return JsonSerializer.Deserialize<T>(json, Json) ?? throw new InvalidDataException($"{file}: empty."); }
        catch (JsonException ex) { throw new InvalidDataException($"{file}: {ex.Message}", ex); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    private sealed record TerrainFile(TerrainDef[]? Terrain);
    private sealed record UnitFile(UnitDef[]? Units);
}
