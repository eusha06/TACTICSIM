using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TacticSim.Sim;
using TacticSim.Sim.Ai;
using TacticSim.Web.Data;

namespace TacticSim.Web.Services;

public sealed record MatchSummaryV2(Guid Id, string Scenario, string Difficulty, string RulesVersion, int Turn,
    string Status, int VpBlue, int VpRed, long Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt);

/// <summary>What a player receives. State and replay are always fog-filtered for Blue.</summary>
public sealed record MatchPayload(MatchSummaryV2 Match, TacticSim.Sim.MatchView State, ReplayView? Replay = null);

public sealed record V2Reply(int Status, MatchPayload? Payload = null, string? Error = null, int? UnitId = null);

public sealed record TurnRequest(Guid CommandId, long ExpectedVersion, Order[]? Orders);

/// <summary>Persisted v2 matches. The visitor always commands Blue; the server plans Red.</summary>
public sealed class MatchV2Service(MatchDbContext db)
{
    private const Side Player = Side.Blue;
    private record StoredReplay(TickRecord[] Ticks, SimEvent[] EndEvents, int VpBlue, int VpRed);

    public async Task<V2Reply> Create(Guid owner, string? scenario, string? difficulty, CancellationToken ct)
    {
        var content = GameContent.Load();
        scenario ??= "rivergate";
        if (!content.Maps.ContainsKey(scenario)) return new(400, Error: $"Unknown scenario '{scenario}'. Choose rivergate or bootcamp.");
        if (!Enum.TryParse<Difficulty>(difficulty ?? "Veteran", ignoreCase: true, out var level) || !Enum.IsDefined(level))
            return new(400, Error: "Difficulty must be Recruit, Veteran or Commander.");
        var now = Now();
        // The seed is drawn once here and saved, so the AI stays deterministic for this match.
        var seed = RandomNumberGenerator.GetInt32(1, int.MaxValue);
        var state = Scenario.Create(content, scenario, seed);
        var match = new MatchV2
        {
            Id = Guid.NewGuid(), OwnerId = owner, Scenario = scenario, Difficulty = level.ToString(),
            RulesVersion = content.RulesVersion, AiSeed = seed, Turn = state.Turn, Version = 1,
            CreatedAt = now, UpdatedAt = now
        };
        db.MatchesV2.Add(match);
        await db.SaveChangesAsync(ct);
        return new(201, Payload(match, state));
    }

    public async Task<V2Reply> Get(Guid owner, Guid id, CancellationToken ct)
    {
        var match = await Owned(owner, id, ct);
        return match is null ? NotFound() : new(200, Payload(match, await CurrentState(match, ct)));
    }

    public async Task<IReadOnlyList<MatchSummaryV2>> List(Guid owner, CancellationToken ct) =>
        (await db.MatchesV2.AsNoTracking().Where(m => m.OwnerId == owner)
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).ToListAsync(ct)).Select(Summary).ToList();

    public async Task<V2Reply> GetTurn(Guid owner, Guid id, int turn, CancellationToken ct)
    {
        var match = await Owned(owner, id, ct);
        if (match is null) return NotFound();
        var stored = await db.TurnResultsV2.AsNoTracking().SingleOrDefaultAsync(r => r.MatchId == id && r.Turn == turn, ct);
        if (stored is null) return new(404, Error: $"Turn {turn} hasn't been played yet.");
        var (result, content) = Restore(stored, match);
        return new(200, Payload(match, result.FinalState) with { Replay = Fog.ReplayFor(content, result, Player) });
    }

    public async Task<V2Reply> SubmitTurn(Guid owner, Guid id, TurnRequest request, CancellationToken ct)
    {
        if (request.CommandId == Guid.Empty) return new(400, Error: "A command ID is required.");
        if (request.Orders is { Length: > 32 } || request.Orders?.Any(o => o.Path is { Length: > 16 }) == true)
            return new(400, Error: "Too many orders or path steps.");
        var orders = request.Orders ?? [];
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { id, request.ExpectedVersion, orders }, SimJson.Options))));

        var saved = await SavedCommand(owner, request.CommandId, fingerprint, ct);
        if (saved is not null) return saved;
        var match = await Owned(owner, id, ct, track: true);
        if (match is null) return NotFound();
        var state = await CurrentState(match, ct);
        if (match.Status != "active") return new(409, Payload(match, state), "This match is over. Start a new one from the menu.");
        if (request.ExpectedVersion != match.Version)
            return new(409, Payload(match, state), "This match changed in another tab. The battlefield has been refreshed.");

        var content = GameContent.Load(match.RulesVersion);
        var blue = OrderRules.Validate(content, state, Player, orders);
        if (!blue.Ok) return new(422, Payload(match, state), blue.Error, blue.UnitId);
        var level = Enum.Parse<Difficulty>(match.Difficulty);
        var planned = AiPlanner.Plan(content, Fog.StateFor(content, state, Side.Red), level, match.AiSeed);
        var red = OrderRules.Validate(content, state, Side.Red, planned);
        if (!red.Ok) red = OrderRules.Validate(content, state, Side.Red, []); // never trust even our own AI blindly
        var result = TurnResolver.Resolve(content, state, blue.Orders, red.Orders);
        var final = result.FinalState;

        var now = Now();
        db.TurnOrdersV2.Add(new TurnOrdersV2
        {
            MatchId = id, Turn = state.Turn, Side = "blue", OwnerId = owner, CommandId = request.CommandId,
            Fingerprint = fingerprint, Orders = Json(blue.Orders), SubmittedAt = now
        });
        db.TurnOrdersV2.Add(new TurnOrdersV2
        {
            MatchId = id, Turn = state.Turn, Side = "red", OwnerId = owner, Orders = Json(red.Orders), SubmittedAt = now
        });
        db.TurnResultsV2.Add(new TurnResultV2
        {
            MatchId = id, Turn = state.Turn, CreatedAt = now, EndSnapshot = Json(final),
            Replay = Json(new StoredReplay(result.Ticks, result.EndEvents, result.VpBlue, result.VpRed))
        });
        match.Turn = final.Turn;
        match.VpBlue = final.VpBlue;
        match.VpRed = final.VpRed;
        match.Status = Status(final);
        match.Version++;
        match.UpdatedAt = now;
        if (final.IsOver) match.CompletedAt = now;
        try
        {
            // One SaveChanges = one transaction: both order sets, the result and the match row.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException ||
            ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return await SavedCommand(owner, request.CommandId, fingerprint, ct) ??
                new(409, (await Get(owner, id, ct)).Payload, "Another turn was saved first. The battlefield has been refreshed.");
        }
        return new(200, Payload(match, final) with { Replay = Fog.ReplayFor(content, result, Player) });
    }

    /// <summary>Re-running the saved orders from the scenario must reproduce every saved snapshot.</summary>
    public async Task<bool> ReplayMatches(Guid id, CancellationToken ct)
    {
        var match = await db.MatchesV2.AsNoTracking().SingleAsync(m => m.Id == id, ct);
        var content = GameContent.Load(match.RulesVersion);
        var orders = await db.TurnOrdersV2.AsNoTracking().Where(o => o.MatchId == id).ToListAsync(ct);
        var results = await db.TurnResultsV2.AsNoTracking().Where(r => r.MatchId == id).OrderBy(r => r.Turn).ToListAsync(ct);
        var state = Scenario.Create(content, match.Scenario, match.AiSeed);
        foreach (var saved in results)
        {
            Order[] For(string side) => Parse<Order[]>(orders.Single(o => o.Turn == saved.Turn && o.Side == side).Orders);
            state = TurnResolver.Resolve(content, state, For("blue"), For("red")).FinalState;
            if (Json(state) != Json(Parse<MatchState>(saved.EndSnapshot))) return false;
        }
        return true;
    }

    private async Task<V2Reply?> SavedCommand(Guid owner, Guid commandId, string fingerprint, CancellationToken ct)
    {
        var receipt = await db.TurnOrdersV2.AsNoTracking()
            .SingleOrDefaultAsync(o => o.OwnerId == owner && o.CommandId == commandId, ct);
        if (receipt is null) return null;
        if (receipt.Fingerprint != fingerprint)
            return new(409, Error: "This command ID was already used for different orders. Reload and try again.");
        // An identical retry: hand back the saved outcome of that turn, never apply it twice.
        return await GetTurn(owner, receipt.MatchId, receipt.Turn, ct);
    }

    private async Task<MatchState> CurrentState(MatchV2 match, CancellationToken ct)
    {
        var last = await db.TurnResultsV2.AsNoTracking().Where(r => r.MatchId == match.Id)
            .OrderByDescending(r => r.Turn).Select(r => r.EndSnapshot).FirstOrDefaultAsync(ct);
        return last is null ? Scenario.Create(GameContent.Load(match.RulesVersion), match.Scenario, match.AiSeed) : Parse<MatchState>(last);
    }

    private (TurnResult Result, GameContent Content) Restore(TurnResultV2 stored, MatchV2 match)
    {
        var replay = Parse<StoredReplay>(stored.Replay);
        var final = Parse<MatchState>(stored.EndSnapshot);
        return (new TurnResult(stored.Turn, replay.Ticks, replay.EndEvents, final, replay.VpBlue, replay.VpRed),
            GameContent.Load(match.RulesVersion));
    }

    private Task<MatchV2?> Owned(Guid owner, Guid id, CancellationToken ct, bool track = false) =>
        (track ? db.MatchesV2 : db.MatchesV2.AsNoTracking()).SingleOrDefaultAsync(m => m.Id == id && m.OwnerId == owner, ct);

    private static MatchPayload Payload(MatchV2 match, MatchState state) =>
        new(Summary(match), Fog.StateFor(GameContent.Load(match.RulesVersion), state, Player));

    private static MatchSummaryV2 Summary(MatchV2 m) => new(m.Id, m.Scenario, m.Difficulty, m.RulesVersion, m.Turn,
        m.Status, m.VpBlue, m.VpRed, m.Version, m.CreatedAt, m.UpdatedAt, m.CompletedAt);

    // A guessed or foreign match id looks exactly like a missing one.
    private static V2Reply NotFound() => new(404, Error: "That match was not found in this browser.");

    private static string Status(MatchState s) => s.Result switch
    {
        MatchResult.BlueWins => "blueWins", MatchResult.RedWins => "redWins", MatchResult.Draw => "draw", _ => "active"
    };

    private static DateTimeOffset Now() => DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, SimJson.Options);
    private static T Parse<T>(string json) => JsonSerializer.Deserialize<T>(json, SimJson.Options)!;
}
