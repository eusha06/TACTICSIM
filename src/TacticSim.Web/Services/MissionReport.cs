using TacticSim.Core;

namespace TacticSim.Web.Services;

// A read-only summary of recorded state, not a new rule, score or saved counter.
public sealed record MissionReport(string Outcome, int CompletedRounds, string RoundDescription,
    int FriendlySurvivors, int FriendlyTotal, int OpposingSurvivors, int OpposingTotal, string CaptureResult)
{
    public static MissionReport From(Game game)
    {
        var capture = game.Mission == MissionType.Rivergate;
        var rounds = (game.TurnNumber - 1) / 2;
        var outcome = game.Winner is null ? "In progress" : game.Winner == Side.Human
            ? capture ? "Mission success" : "Victory" : capture ? "Mission failed" : "Defeat";
        var roundDescription = $"{rounds} completed {(rounds == 1 ? "round" : "rounds")}.";
        // A capture win is recorded after the computer completes its round. An
        // elimination can finish during either side's turn in an incomplete round.
        if (game.Winner is not null && !(capture && game.Winner == Side.Human))
            roundDescription += $" Ended during round {rounds + 1}.";
        var captureResult = !capture ? "Not part of this legacy elimination match." :
            $"{(game.HeldRounds == Game.RequiredHoldRounds ? "Secured" : game.Winner is not null ? "Not secured" : game.OutpostHolder is not null ? "Holding" : "Not occupied")} · {game.HeldRounds}/{Game.RequiredHoldRounds} rounds";
        return new(outcome, rounds, roundDescription,
            game.Units.Count(u => u.Side == Side.Human && u.IsAlive), game.Units.Count(u => u.Side == Side.Human),
            game.Units.Count(u => u.Side == Side.Computer && u.IsAlive), game.Units.Count(u => u.Side == Side.Computer), captureResult);
    }
}
