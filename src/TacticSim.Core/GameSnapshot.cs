namespace TacticSim.Core;

// Plain data for saving a game; no JSON, EF, or database dependency in Core.
public sealed record UnitSnapshot(string Type, int Id, Side Side, Position Position,
    int Health, bool HasMoved, bool HasAttacked);

public sealed record GameSnapshot(Side CurrentSide, int TurnNumber,
    Terrain[] Tiles, UnitSnapshot[] Units);

public enum ActionKind { Move, Attack, EndTurn }

public sealed record GameAction(int TurnNumber, Side Side, ActionKind Kind,
    int? UnitId = null, int? TargetId = null, Position? Destination = null,
    int Damage = 0, bool Destroyed = false);
