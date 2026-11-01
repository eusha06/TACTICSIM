namespace TacticSim.Core;

public enum GameError
{
    None,
    GameOver,
    NotYourTurn,
    UnitNotFound,
    NotYourUnit,
    UnitDestroyed,
    AlreadyMoved,
    AlreadyAttacked,
    MoveAfterAttack,
    OutOfBounds,
    ImpassableTerrain,
    DestinationOccupied,
    Unreachable,
    TargetNotFound,
    TargetDestroyed,
    FriendlyFire,
    OutOfRange
}

// Invalid commands are normal input errors, not exceptions.
public readonly record struct ActionResult(GameError Error, int Damage = 0)
{
    public bool IsSuccess => Error == GameError.None;
}
