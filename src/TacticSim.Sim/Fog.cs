namespace TacticSim.Sim;

public sealed record UnitView(int Id, Side Side, string Type, Hex Pos, int Hp, int MaxHp);

/// <summary>A match as one side may see it. Hidden enemy units are simply absent.</summary>
public sealed record MatchView(string Scenario, string RulesVersion, Side Viewer, int Turn, int TurnLimit, int VpToWin,
    int VpBlue, int VpRed, MatchResult? Result, string? EndReason, UnitView[] Units, LastSeenMarker[] LastSeen,
    Dictionary<string, Side?> Control, int[] Visible, VpPoint[] VpHistory, int Kills, int Losses, bool FogLifted);

public sealed record TickView(int Tick, UnitFrame[] Units, SimEvent[] Events, int[] Visible);

public sealed record ReplayView(int Turn, TickView[] Ticks, SimEvent[] EndEvents, string[] Debrief);

/// <summary>
/// Server-side fog of war. Everything sent to a player goes through here, so hidden enemy
/// units, their moves and their orders never leave the server.
/// </summary>
public static class Fog
{
    public static MatchView StateFor(GameContent content, MatchState state, Side viewer)
    {
        var board = content.Board(state.Scenario);
        var view = Vision.ViewOf(content, board, state.Units, viewer);
        // Once the match is over there is nothing left to hide: the after-action map shows everyone.
        var lifted = state.IsOver;
        var units = state.Units.Where(u => u.Alive && (lifted || u.Side == viewer || view.Sees(u))).OrderBy(u => u.Id)
            .Select(u => new UnitView(u.Id, u.Side, u.Type, u.Pos, u.Hp, content.Units[u.Type].Hp)).ToArray();
        var visible = lifted ? Enumerable.Range(0, board.Width * board.Height).ToArray()
            : view.VisibleHexes.Select(board.Index).Order().ToArray();
        var losses = state.Units.Count(u => u.Side == viewer && !u.Alive);
        return new MatchView(state.Scenario, state.RulesVersion, viewer, state.Turn, board.Map.TurnLimit, board.Map.VpToWin,
            state.VpBlue, state.VpRed, state.Result, state.EndReason, units, lifted ? [] : state.LastSeen(viewer).ToArray(),
            new Dictionary<string, Side?>(state.Control), visible, state.VpHistory.ToArray(),
            viewer == Side.Blue ? state.BlueKills : state.RedKills, losses, lifted);
    }

    public static ReplayView ReplayFor(GameContent content, TurnResult result, Side viewer)
    {
        var board = content.Board(result.FinalState.Scenario);
        var owner = result.FinalState.Units.ToDictionary(u => u.Id, u => u.Side);
        var ticks = new List<TickView>();
        foreach (var tick in result.Ticks)
        {
            var sees = (viewer == Side.Blue ? tick.BlueSees : tick.RedSees).ToHashSet();
            var visibleIndexes = viewer == Side.Blue ? tick.BlueVisible : tick.RedVisible;
            var visible = visibleIndexes.Select(board.FromIndex).ToHashSet();
            bool Known(int? id) => id is int i && (owner[i] == viewer || sees.Contains(i));
            var events = new List<SimEvent>();
            foreach (var e in tick.Events)
                if (FilterEvent(e, viewer, Known, owner, visible) is { } kept) events.Add(kept);
            ticks.Add(new TickView(tick.Tick, tick.Units.Where(u => Known(u.Id)).ToArray(), events.ToArray(), visibleIndexes));
        }
        var replay = new ReplayView(result.Turn, ticks.ToArray(), result.EndEvents, []);
        return replay with { Debrief = Debrief(content, result, replay, viewer, owner) };
    }

    private static SimEvent? FilterEvent(SimEvent e, Side viewer, Func<int?, bool> known,
        IReadOnlyDictionary<int, Side> owner, HashSet<Hex> visible)
    {
        switch (e.Kind)
        {
            case EventKind.Spotted:
            case EventKind.Lost:
                return e.Side == viewer ? e : null;
            case EventKind.Move:
            case EventKind.GiveWay:
                if (!known(e.UnitId)) return null;
                // An enemy stepping out of the fog appears; where it came from stays hidden.
                return e.From is Hex from && !visible.Contains(from) && owner[e.UnitId!.Value] != viewer ? e with { From = null } : e;
            case EventKind.Blocked:
            case EventKind.Contact:
                if (!known(e.UnitId)) return null;
                return known(e.TargetId) ? e : e with { TargetId = null };
            case EventKind.Shot:
            case EventKind.Shell:
                if (!known(e.TargetId)) return null;
                return known(e.UnitId) ? e : e with { UnitId = null, From = null };
            case EventKind.Barrage:
                var hexSeen = e.To is Hex hex && visible.Contains(hex);
                var shooterKnown = known(e.UnitId);
                if (!shooterKnown && !hexSeen && !known(e.TargetId)) return null;
                var shown = shooterKnown ? e : e with { UnitId = null, From = null };
                return known(e.TargetId) ? shown : shown with { TargetId = null, Damage = 0, Hp = 0, Destroyed = false };
            case EventKind.Destroyed:
                return known(e.TargetId) ? e : null;
            default:
                return e;
        }
    }

    private static string[] Debrief(GameContent content, TurnResult result, ReplayView replay, Side viewer,
        IReadOnlyDictionary<int, Side> owner)
    {
        var map = content.Board(result.FinalState.Scenario).Map;
        var final = result.FinalState;
        var types = result.FinalState.Units.ToDictionary(u => u.Id, u => content.Units[u.Type].Name);
        var events = replay.Ticks.SelectMany(t => t.Events).ToList();
        string Label(string id) => $"{id} · {map.Objectives.First(o => o.Id == id).Label}";
        var lines = new List<string>();

        if (final.IsOver)
        {
            var won = final.Result == (viewer == Side.Blue ? MatchResult.BlueWins : MatchResult.RedWins);
            var how = final.EndReason switch
            {
                "annihilation" => "every unit on one side was destroyed",
                "vp" => $"a side reached {map.VpToWin} VP",
                _ => $"turn {map.TurnLimit} ended"
            };
            lines.Add(final.Result == MatchResult.Draw ? $"Draw: {how} with the score level." : won ? $"Victory: {how}." : $"Defeat: {how}.");
        }

        int mine = viewer == Side.Blue ? result.VpBlue : result.VpRed, theirs = viewer == Side.Blue ? result.VpRed : result.VpBlue;
        var held = final.Control.Where(c => c.Value == viewer).Select(c => c.Key).ToList();
        lines.Add(mine + theirs == 0 ? "No VP this turn: nobody controls an objective."
            : $"VP: you +{mine}{(held.Count > 0 ? $" ({string.Join(", ", held)})" : "")}, enemy +{theirs}. " +
              $"Score {final.Vp(viewer)}–{final.Vp(viewer.Enemy())}.");

        foreach (var c in result.EndEvents.Where(e => e.Kind == EventKind.Control))
            lines.Add(c.Reason switch
            {
                "captured" => $"{(c.Side == viewer ? "You" : "The enemy")} took {Label(c.Objective!)}.",
                "contested" => $"{Label(c.Objective!)} is contested: both sides are next to it.",
                _ => $"{Label(c.Objective!)} is unheld now."
            });

        var destroyed = events.Where(e => e.Kind == EventKind.Destroyed).Select(e => e.TargetId!.Value).ToList();
        var killed = destroyed.Where(id => owner[id] != viewer).Select(id => types[id]).ToList();
        var lost = destroyed.Where(id => owner[id] == viewer).Select(id => types[id]).ToList();
        if (killed.Count > 0) lines.Add($"Destroyed: enemy {string.Join(", ", killed)}.");
        if (lost.Count > 0) lines.Add($"Lost: your {string.Join(", ", lost)}.");

        var hits = events.Where(e => e.Kind is EventKind.Shot or EventKind.Shell or EventKind.Barrage && e.TargetId is not null).ToList();
        var dealt = hits.Where(e => owner[e.TargetId!.Value] != viewer).Sum(e => e.Damage);
        var taken = hits.Where(e => owner[e.TargetId!.Value] == viewer).Sum(e => e.Damage);
        if (dealt + taken > 0) lines.Add($"Damage: you dealt {dealt} and took {taken}.");

        var spotted = events.Where(e => e.Kind == EventKind.Spotted && owner[e.TargetId!.Value] != viewer)
            .GroupBy(e => e.TargetId).Select(g => $"enemy {types[g.Key!.Value]} at {g.First().To}").ToList();
        if (spotted.Count > 0) lines.Add($"Spotted: {string.Join(", ", spotted.Take(3))}{(spotted.Count > 3 ? " and more" : "")}.");
        if (hits.Count == 0 && destroyed.Count == 0) lines.Add("No shots landed this turn.");
        return lines.Take(5).ToArray();
    }
}
