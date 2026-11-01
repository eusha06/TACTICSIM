using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TacticSim.Core;
using TacticSim.Web.Data;

namespace TacticSim.Web.Services;

public sealed record MatchCommand(Guid CommandId, long ExpectedVersion, string Kind,
    int? UnitId = null, int? TargetId = null, int? X = null, int? Y = null,
    Guid? MatchId = null, long? ExpectedMatchVersion = null, Guid? TargetMatchId = null);
public sealed record HistoryEntry(int Turn, string Side, string Text, long Sequence);
public sealed record MoveView(int X, int Y, int Cost);
public sealed record UnitView(int Id, string Type, string Side, int X, int Y, int Health,
    int MaxHealth, int MovementPoints, int MinimumRange, int MaximumRange, int BaseDamage,
    bool HasMoved, bool HasAttacked, MoveView[] LegalMoves, int[] LegalTargets);
public sealed record ResultView(string Winner, int Turns, DateTimeOffset CompletedAt);
public sealed record MatchView(Guid MatchId, long Version, long MatchVersion, string CurrentSide, int TurnNumber,
    string? Winner, string[] Tiles, UnitView[] Units, HistoryEntry[] History, ResultView[] Results);
public sealed record MatchSummary(Guid MatchId, long MatchVersion, int TurnNumber, string? Winner,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, bool IsActive, long EventCount);
public sealed record ServiceReply(int Status, MatchView? State, string? Error = null);

public sealed class MatchService(MatchDbContext db)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Terrain[] MapTiles = Game.CreateDefault().ExportSnapshot().Tiles;

    private IQueryable<SavedMatch> OwnedMatches(Guid owner) => db.Matches
        .Where(m => m.OwnerId == owner).Include(m => m.Units).Include(m => m.Events).AsSplitQuery();

    public async Task<MatchView?> Read(Guid owner, CancellationToken ct)
    {
        var session = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(s => s.OwnerId == owner, ct);
        if (session?.ActiveMatchId is not Guid matchId) return null;
        return await ReadMatch(owner, matchId, ct);
    }

    public async Task<MatchView?> ReadMatch(Guid owner, Guid matchId, CancellationToken ct)
    {
        var match = await OwnedMatches(owner).AsNoTracking().SingleOrDefaultAsync(m => m.Id == matchId, ct);
        if (match is null) return null;
        var revision = await db.Sessions.Where(s => s.OwnerId == owner).Select(s => s.Revision).SingleAsync(ct);
        return await View(revision, match, ct);
    }

    public async Task<IReadOnlyList<MatchSummary>> History(Guid owner, CancellationToken ct)
    {
        var active = await db.Sessions.Where(s => s.OwnerId == owner).Select(s => s.ActiveMatchId).SingleOrDefaultAsync(ct);
        return await db.Matches.AsNoTracking().Where(m => m.OwnerId == owner)
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .Select(m => new MatchSummary(m.Id, m.Version, m.TurnNumber, m.Winner == null ? null : m.Winner.ToString(),
                m.CreatedAt, m.UpdatedAt, m.Id == active, m.LastEventSequence)).ToListAsync(ct);
    }

    public async Task<ServiceReply> Execute(Guid owner, MatchCommand command, CancellationToken ct)
    {
        if (command.CommandId == Guid.Empty || command.ExpectedVersion < 0)
            return new(400, null, "A command ID and a valid version are required.");
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(command, Json))));
        var receipt = await db.Receipts.AsNoTracking()
            .SingleOrDefaultAsync(r => r.OwnerId == owner && r.CommandId == command.CommandId, ct);
        if (receipt is not null) return Replay(receipt, fingerprint);

        var session = await db.Sessions.SingleOrDefaultAsync(s => s.OwnerId == owner, ct);
        if (command.ExpectedVersion != (session?.Revision ?? 0))
            return await ConflictOrReplay(owner, command.CommandId, fingerprint,
                "This session changed in another tab. The board has been refreshed.", ct);
        var current = session?.ActiveMatchId is Guid activeId
            ? await OwnedMatches(owner).SingleOrDefaultAsync(m => m.Id == activeId, ct) : null;
        if (current is not null && (command.MatchId != current.Id || command.ExpectedMatchVersion != current.Version))
            return await ConflictOrReplay(owner, command.CommandId, fingerprint,
                "This match changed or another match is active. Refresh before acting.", ct);
        if (current is null && command.Kind != "new") return new(404, null, "Start a new match first.");

        var now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        SavedMatch match;
        if (command.Kind == "new")
        {
            if (session is null)
            {
                session = new BrowserSession { OwnerId = owner };
                db.Sessions.Add(session);
            }
            match = new SavedMatch { Id = Guid.NewGuid(), OwnerId = owner, Version = 1, CreatedAt = now, UpdatedAt = now };
            CopyState(match, Game.CreateDefault());
            db.Matches.Add(match);
            AppendEvent(match, command.CommandId, "MatchStarted", 1, Side.Human,
                "Match started. Your blue squad moves first.", now);
        }
        else if (command.Kind == "resume")
        {
            // A target ID never bypasses ownership, even for the same browser's old tabs.
            var target = await OwnedMatches(owner).SingleOrDefaultAsync(m => m.Id == command.TargetMatchId, ct);
            if (target is null) return new(404, null, "That match was not found in this browser session.");
            match = target;
            if (match.Winner is not null) return new(422, null, "Completed matches can be reviewed, but cannot be resumed.");
            match.Version++;
            match.UpdatedAt = now;
            AppendEvent(match, command.CommandId, "MatchResumed", match.TurnNumber, Side.Human,
                "Match resumed. Unit action limits are unchanged.", now);
        }
        else
        {
            match = current!;
            var game = Restore(match);
            // Visitors always command Human; side and owner are never taken from JSON.
            var result = command.Kind switch
            {
                "move" when command.UnitId is int id && command.X is int x && command.Y is int y =>
                    game.Move(Side.Human, id, new Position(x, y)),
                "attack" when command.UnitId is int id && command.TargetId is int target =>
                    game.Attack(Side.Human, id, target),
                "endTurn" => game.EndTurn(Side.Human),
                _ => (ActionResult?)null
            };
            if (result is null) return new(400, await View(session!.Revision, match, ct), "Unknown command or missing action fields.");
            if (!result.Value.IsSuccess) return new(422, await View(session!.Revision, match, ct), Explain(result.Value.Error));
            if (command.Kind == "endTurn" && game.Winner is null)
            {
                var computer = new ComputerOpponent().PlayTurn(game);
                if (!computer.IsSuccess) throw new InvalidOperationException("Computer turn could not complete.");
            }
            CopyState(match, game);
            match.Version++;
            match.UpdatedAt = now;
            if (game.Winner is not null) match.CompletedAt = now;
            foreach (var action in game.Actions)
            {
                var entry = AppendEvent(match, command.CommandId, action.Kind.ToString(), action.TurnNumber,
                    action.Side, Describe(game, action), now);
                entry.UnitId = action.UnitId;
                entry.TargetId = action.TargetId;
                entry.X = action.Destination?.X;
                entry.Y = action.Destination?.Y;
                entry.Damage = action.Damage;
                entry.Destroyed = action.Destroyed;
            }
        }

        session!.ActiveMatchId = match.Id;
        session.Revision++;
        var state = await View(session.Revision, match, ct);
        db.Receipts.Add(new CommandReceipt
        {
            OwnerId = owner, CommandId = command.CommandId, MatchId = match.Id, Kind = command.Kind,
            UnitId = command.UnitId, TargetId = command.TargetId, X = command.X, Y = command.Y,
            TargetMatchId = command.TargetMatchId, ExpectedMatchVersion = command.ExpectedMatchVersion,
            ResultingMatchVersion = match.Version, AcceptedAt = now, Fingerprint = fingerprint,
            ResponseJson = JsonSerializer.Serialize(state, Json)
        });
        try
        {
            // EF's single SaveChanges transaction commits session cursor, match, units,
            // ordered events and receipt together. Both revisions are concurrency tokens.
            await db.SaveChangesAsync(ct);
            return new(200, state);
        }
        catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException ||
            ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            receipt = await db.Receipts.AsNoTracking()
                .SingleOrDefaultAsync(r => r.OwnerId == owner && r.CommandId == command.CommandId, ct);
            return receipt is not null ? Replay(receipt, fingerprint) :
                new(409, await Read(owner, ct), "Another command was saved first. The board has been refreshed.");
        }
    }

    private static ServiceReply Replay(CommandReceipt receipt, string fingerprint) =>
        receipt.Fingerprint == fingerprint
            ? new(200, JsonSerializer.Deserialize<MatchView>(receipt.ResponseJson, Json))
            : new(409, null, "This command ID was already used for a different action. Refresh and try again.");

    private async Task<ServiceReply> ConflictOrReplay(Guid owner, Guid commandId, string fingerprint,
        string message, CancellationToken ct)
    {
        // Another request may commit this SAME command after our first receipt
        // lookup, but before we read its updated version. Prefer its saved result.
        var receipt = await db.Receipts.AsNoTracking()
            .SingleOrDefaultAsync(r => r.OwnerId == owner && r.CommandId == commandId, ct);
        return receipt is not null ? Replay(receipt, fingerprint) : new(409, await Read(owner, ct), message);
    }

    private static Game Restore(SavedMatch saved)
    {
        if (saved.MapKey != "crossing-v1") throw new InvalidOperationException("Unknown saved map.");
        return Game.Restore(new GameSnapshot(saved.CurrentSide, saved.TurnNumber, MapTiles,
            saved.Units.OrderBy(u => u.UnitId).Select(u => new UnitSnapshot(u.Type, u.UnitId, u.Side,
                new Position(u.X, u.Y), u.Health, u.HasMoved, u.HasAttacked)).ToArray()));
    }

    private static void CopyState(SavedMatch saved, Game game)
    {
        saved.CurrentSide = game.CurrentSide;
        saved.TurnNumber = game.TurnNumber;
        saved.Winner = game.Winner;
        foreach (var unit in game.Units)
        {
            var row = saved.Units.SingleOrDefault(u => u.UnitId == unit.Id);
            if (row is null)
            {
                row = new SavedUnit { MatchId = saved.Id, UnitId = unit.Id, Type = unit.GetType().Name, Side = unit.Side };
                saved.Units.Add(row);
            }
            row.X = unit.Position.X;
            row.Y = unit.Position.Y;
            row.Health = unit.Health;
            row.HasMoved = unit.HasMoved;
            row.HasAttacked = unit.HasAttacked;
        }
    }

    private static SavedEvent AppendEvent(SavedMatch match, Guid commandId, string kind,
        int turn, Side side, string text, DateTimeOffset now)
    {
        var entry = new SavedEvent { MatchId = match.Id, Sequence = ++match.LastEventSequence,
            CommandId = commandId, Turn = turn, Side = side, Kind = kind, Text = text, OccurredAt = now };
        match.Events.Add(entry);
        return entry;
    }

    private async Task<MatchView> View(long revision, SavedMatch saved, CancellationToken ct)
    {
        var game = Restore(saved);
        var results = await db.Matches.AsNoTracking()
            .Where(m => m.OwnerId == saved.OwnerId && m.Winner != null && m.Id != saved.Id)
            .OrderByDescending(m => m.CompletedAt).Take(10)
            .Select(m => new ResultView(m.Winner.ToString()!, m.TurnNumber, m.CompletedAt!.Value)).ToListAsync(ct);
        if (saved.Winner is not null && saved.CompletedAt is DateTimeOffset completed)
            results.Add(new(saved.Winner.ToString()!, saved.TurnNumber, completed));
        return new(saved.Id, revision, saved.Version, game.CurrentSide.ToString(), game.TurnNumber,
            game.Winner?.ToString(), MapTiles.Select(t => t.ToString()).ToArray(),
            game.Units.Select(u => new UnitView(u.Id, u.GetType().Name, u.Side.ToString(),
                u.Position.X, u.Position.Y, u.Health, u.MaxHealth, u.MovementPoints,
                u.MinimumRange, u.MaximumRange, u.BaseDamage, u.HasMoved, u.HasAttacked,
                game.GetLegalMoves(Side.Human, u.Id).Select(cell => new MoveView(cell.Key.X, cell.Key.Y, cell.Value)).ToArray(),
                game.GetLegalTargets(Side.Human, u.Id).Select(target => target.Id).ToArray())).ToArray(),
            saved.Events.OrderBy(e => e.Sequence).Select(e => new HistoryEntry(e.Turn, e.Side.ToString(), e.Text, e.Sequence)).ToArray(),
            results.OrderByDescending(r => r.CompletedAt).Take(10).ToArray());
    }

    private static string Describe(Game game, GameAction action)
    {
        string Name(int? id) => $"{game.Units.Single(u => u.Id == id).GetType().Name} #{id}";
        var who = action.Side == Side.Human ? "You" : "Opponent";
        return action.Kind switch
        {
            ActionKind.Move => $"{who}: {Name(action.UnitId)} moved to {(char)('A' + action.Destination!.Value.X)}{action.Destination.Value.Y + 1}.",
            ActionKind.Attack => $"{who}: {Name(action.UnitId)} hit {Name(action.TargetId)} for {action.Damage} damage.{(action.Destroyed ? " Target destroyed." : "")}",
            _ => $"{who} ended the turn."
        };
    }

    private static string Explain(GameError error) => error switch
    {
        GameError.GameOver => "This match is complete. Start a new match to play again.",
        GameError.NotYourTurn => "It is not your turn. Refresh the board.",
        GameError.NotYourUnit => "You can only command your blue units.",
        GameError.UnitNotFound or GameError.TargetNotFound => "That unit does not exist.",
        GameError.UnitDestroyed or GameError.TargetDestroyed => "That unit has been destroyed.",
        GameError.AlreadyMoved => "This unit already moved this turn.",
        GameError.AlreadyAttacked => "This unit already attacked this turn.",
        GameError.MoveAfterAttack => "A unit cannot move after attacking.",
        GameError.OutOfBounds => "Choose a cell inside the board.",
        GameError.ImpassableTerrain => "Water is impassable.",
        GameError.DestinationOccupied => "A living unit occupies that cell.",
        GameError.Unreachable => "That cell cannot be reached within this unit's movement budget.",
        GameError.FriendlyFire => "Choose an orange opponent as your attack target.",
        GameError.OutOfRange => "That target is outside this unit's attack range.",
        _ => "That action could not be completed."
    };
}
