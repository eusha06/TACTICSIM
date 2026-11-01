# MVP game rules

Coordinates are zero-based `(x, y)` from the top left. Each new match uses this fixed 8x8 map (`.` plain, `F` forest, `H` hill, `W` water):

```text
........
..FF.H..
.H..W...
...WW.F.
.F.WW...
...W..H.
..H.FF..
........
```

Human starts with Infantry at `(0,0)`, Armor at `(1,0)`, and Artillery at `(2,0)` (IDs 1–3). Computer starts with Infantry at `(7,7)`, Armor at `(6,7)`, and Artillery at `(5,7)` (IDs 4–6). Human takes the first turn. Custom 8x8 scenarios are supported for engine tests.

| Unit | Health | Movement points | Attack range (inclusive) | Base damage | Plain cost | Forest cost | Hill cost |
| --- | ---: | ---: | --- | ---: | ---: | ---: | ---: |
| Infantry | 10 | 3 | 1 | 4 | 1 | 2 | 2 |
| Armor | 16 | 4 | 1 | 6 | 1 | 3 | 2 |
| Artillery | 8 | 2 | 2–4 | 5 | 1 | 2 | 3 |

Movement uses only four directions. Dijkstra's algorithm finds the cheapest route, summing the costs of cells entered; the starting cell costs zero. A move must end in a different empty cell within the unit's movement budget. Water and cells occupied by **any living unit** block both crossing and stopping. Dead units do not block movement. Costs above the budget make a cell unreachable, even if it is adjacent (for example, Artillery entering a hill).

Each living unit may move once and attack once per turn. A move consumes its movement action even when it uses less than the full budget. Moving after attacking is forbidden. Moving then attacking is allowed. Failed actions consume neither action and change no state. Ending a turn switches side, increments the turn number (one side's turn, not a full round), and resets the incoming side's living units. Ending a turn is always allowed while the match is active, even with unused actions.

Attack distance is Manhattan distance: `abs(x1 - x2) + abs(y1 - y2)`. Artillery cannot attack adjacent units. **Terrain and units do not block line of sight**, including water between attacker and target. Only living enemy units can be targeted; there is no friendly fire or retaliation.

Terrain defense uses the **target's** cell: plain `0`, forest `1`, hill `2`. Damage is `max(1, attacker.BaseDamage - defense)`. Health becomes `max(0, health - damage)`; the action result reports the actual health removed, capped by remaining health. Damage has no randomness, health scaling, or unit-type multipliers.

Legacy elimination matches: a side wins immediately when every opposing unit has zero health. Destroyed units cannot act or be attacked. A completed match rejects all moves, attacks, and end-turn commands.

The computer processes living units by ascending ID. It attacks immediately when possible; otherwise it chooses among legal moves and staying still, preferring the smallest distance to an enemy's valid attack band, then nearest enemy distance, then cheapest movement cost, then row and column. It attacks the legal target with lowest health, breaking ties by ID, then ends the turn unless it won. It uses the same Game queries and commands as the human. This bounded greedy opponent can wait behind obstacles; it does not plan several turns ahead.

`Game` owns copies of its units and exposes read-only state. Its commands receive the acting side and validate turn/unit ownership. The Web layer derives the match owner from a protected browser cookie and always uses Human for visitor commands; a client-supplied side is not authentication. PostgreSQL stores relational match/unit state and history, while Razor Pages antiforgery and atomic database version/receipt checks protect the HTTP boundary.

## Rivergate mission (Release 3)

New browser matches use `Game.CreateRivergate()`. Existing/imported matches and
`Game.CreateDefault()` keep elimination rules. The map, six starting units,
movement, combat and terrain are unchanged. One communications outpost stands on
**F4 `(5,3)`, plain land**, initially empty. All six starting units can reach it
by legal land routes around the north/east riverbank; tests use the actual
movement query with initial occupancy, not a separate terrain approximation.

Any living friendly unit on F4 holds it. Count **one** round only after the
computer has finished its turn, if that unit is still alive and occupying F4.
Two consecutive such rounds win. Merely arriving, moving, attacking, refreshing,
resuming or retrying a command never increments the count. The engine persists
the last evaluated computer turn; the database transaction/version/receipt also
protect against concurrent or repeated HTTP commands.

Leaving the outpost or the holder dying resets progress immediately, even if
another friendly unit takes over before the round ends. An enemy occupying the
outpost earns no progress. Losing every friendly unit immediately fails the
mission. **Destroying all opponents does not win Rivergate:** keep a friendly
unit on F4 and End Turn until the two holds complete. With no enemies left the
computer still completes its empty turn, so capture cannot become stuck. Defeat
takes precedence if there are no living friendlies; a successful hold always
requires a living holder. Completed games reject further gameplay commands.

For Rivergate only, the deterministic opponent prefers legal firing positions
against the holder, otherwise advances toward the outpost. It attacks the holder
first when in range, then the lowest-health target/ID. All choices pass the same
Core validators. This is a small greedy opponent, not a multi-turn planner.
A verified winning route loses Infantry and Armor under enemy pressure and
captures with Artillery; the outpost is contested rather than a free walk.
