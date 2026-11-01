namespace TacticSim.Core;

public sealed class ComputerOpponent
{
    public ActionResult PlayTurn(Game game)
    {
        ArgumentNullException.ThrowIfNull(game);
        var error = game.ValidateTurn(Side.Computer);
        if (error != GameError.None) return new ActionResult(error);

        foreach (var unit in game.Units.Where(u => u.Side == Side.Computer && u.IsAlive)
                     .OrderBy(u => u.Id))
        {
            if (game.Winner is not null) break;
            var targets = game.GetLegalTargets(Side.Computer, unit.Id);

            // Prefer firing now. Attacking first forbids movement this turn.
            if (targets.Count == 0)
            {
                var enemies = game.Units.Where(u => u.Side == Side.Human && u.IsAlive).ToList();
                var candidates = game.GetLegalMoves(Side.Computer, unit.Id)
                    .Append(new KeyValuePair<Position, int>(unit.Position, 0));
                var destination = candidates
                    .OrderBy(cell => game.Mission == MissionType.Rivergate
                        ? game.OutpostHolder is { } holder
                            ? DistanceToRange(cell.Key.ManhattanDistanceTo(holder.Position), unit)
                            : cell.Key.ManhattanDistanceTo(game.Objective!.Value)
                        : 0)
                    .ThenBy(cell => enemies.Min(enemy =>
                        DistanceToRange(cell.Key.ManhattanDistanceTo(enemy.Position), unit)))
                    .ThenBy(cell => enemies.Min(enemy => cell.Key.ManhattanDistanceTo(enemy.Position)))
                    .ThenBy(cell => cell.Value)
                    .ThenBy(cell => cell.Key.Y)
                    .ThenBy(cell => cell.Key.X)
                    .First().Key;

                if (destination != unit.Position)
                    RequireSuccess(game.Move(Side.Computer, unit.Id, destination));
                targets = game.GetLegalTargets(Side.Computer, unit.Id);
            }

            // Lowest health first, then stable ID. No random choices.
            var target = targets.OrderBy(t => game.Mission == MissionType.Rivergate && t == game.OutpostHolder ? 0 : 1).ThenBy(t => t.Health).ThenBy(t => t.Id).FirstOrDefault();
            if (target is not null)
                RequireSuccess(game.Attack(Side.Computer, unit.Id, target.Id));
        }

        return game.Winner is not null
            ? new ActionResult(GameError.None)
            : game.EndTurn(Side.Computer);
    }

    // This is only a movement heuristic. Game validates every chosen action.
    private static int DistanceToRange(int distance, Unit unit) =>
        distance < unit.MinimumRange ? unit.MinimumRange - distance :
        distance > unit.MaximumRange ? distance - unit.MaximumRange : 0;

    private static void RequireSuccess(ActionResult result)
    {
        if (!result.IsSuccess)
            throw new InvalidOperationException($"Computer chose an illegal action: {result.Error}.");
    }
}
