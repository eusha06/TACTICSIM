namespace TacticSim.Sim;

/// <summary>The damage formula: attack (vs Foot or vs Vehicle) minus target cover, minimum 1. No dice.</summary>
public static class Combat
{
    /// <summary>Cover of the target where it stands: terrain + unit modifier + Hold bonus, never below 0.</summary>
    public static int Cover(GameContent content, Board board, UnitState target, bool holding)
    {
        var terrain = board.TerrainAt(target.Pos);
        var cover = terrain.Cover + content.Units[target.Type].CoverModIn(terrain) + (holding ? content.Rules.HoldCover : 0);
        return Math.Max(0, cover);
    }

    public static int Damage(GameContent content, UnitDef attacker, UnitDef target, int cover) =>
        Math.Max(content.Rules.MinDamage, attacker.AttackVs(target) - cover);

    public static int Damage(GameContent content, Board board, UnitState attacker, UnitState target, bool targetHolding) =>
        Damage(content, content.Units[attacker.Type], content.Units[target.Type], Cover(content, board, target, targetHolding));

    public static bool InRange(UnitDef unit, Hex from, Hex to)
    {
        var distance = HexMath.Distance(from, to);
        return distance >= unit.MinRange && distance <= unit.MaxRange;
    }

    /// <summary>
    /// Direct fire needs range and the shooter's own line of sight. Indirect fire (artillery)
    /// needs range and a target that some friendly unit currently sees.
    /// </summary>
    public static bool CanFireAt(GameContent content, Board board, UnitState shooter, UnitState target, ISet<Hex> sideVisible)
    {
        var unit = content.Units[shooter.Type];
        if (!target.Alive || !InRange(unit, shooter.Pos, target.Pos)) return false;
        return unit.Indirect ? sideVisible.Contains(target.Pos) : Vision.HasLos(board, shooter.Pos, target.Pos);
    }

    /// <summary>Fire-at-will choice: most damage, then lowest remaining HP, then lowest unit id.</summary>
    public static UnitState? BestTarget(GameContent content, Board board, UnitState shooter, IEnumerable<UnitState> targets,
        Func<UnitState, bool> holding) =>
        targets.OrderByDescending(t => Damage(content, board, shooter, t, holding(t)))
            .ThenBy(t => t.Hp).ThenBy(t => t.Id).FirstOrDefault();
}
