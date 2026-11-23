# v2 spec decisions

Ambiguous spec details, the simplest readable choice made, and why.

- **AGENTS.md removed** (user request). Its safety rules moved into `CLAUDE.md`.
- **Content location:** `content/v1/{rules,terrain,units}.json` + `content/v1/maps/*.json`, embedded into
  `TacticSim.Sim` so server, tests and tools read identical files. Turn-level numbers (8 ticks, Hold +1,
  road march +2, barrage tick 6, hill vision +1, min damage 1) live in `rules.json`.
- **Bridge counts as road** for road march. Road march applies when the start hex and every hex entered are road.
- **Movement budget** is capped at the turn length (8 ticks), so Recon on road gets 8, not 9.
- **Occupancy during a tick** is judged from where units stood at the start of that tick. Entering a hex an
  enemy is just leaving still counts as "occupied": the mover is blocked.
- **Friendly pass-through:** you can't step into a friendly's hex as your *final* step. A unit whose path ends
  on a friendly's hex (contact or a block) steps back to the most recent empty hex on its route.
- **Line of sight is two-way.** If either end is a hill, nothing between blocks (hill units see over forest/
  town, and are seen over them). A hill between two non-hill hexes blocks. Target hex terrain never blocks.
  Line hexes that round off the jagged map edge count as open.
- **Contact** = entering a hex adjacent to an enemy you can see. Adjacent hexes are always in sight.
- **Overwatch** fires at the first enemy in range + LOS on any tick 1–8, including one that was already in
  range at tick 1 (otherwise Overwatch would be a trap next to Hold). Best-target rule breaks ties.
- **Attack with a path** fires the first tick the target is in range + LOS, then stops moving.
- **Attack without a path** must be in range (and direct-fire LOS) when ordered; the error names the range.
- **Move with an assigned target** that isn't valid at the end falls back to fire at will.
- **Barrage** only lands if the artillery is alive at tick 6. Artillery with a Move order never fires.
- **Destroyed units** stop at once: they don't move, shoot, see or count for control on later ticks.
- **Control:** only units that can capture (Rifle, AT, Armor) control or contest. Contested = capturers of both
  sides on or adjacent. Control is recalculated every turn (no "sticky" control).
- **Fog in replays:** an enemy stepping out of the fog keeps its `from` hex hidden. Fire from an unseen shooter
  shows the hit but not the shooter. A shell on a hidden unit shows only the explosion. Kills count only when
  the shooter's side could see the victim. Once the match ends, `/state` lifts the fog for the after-action map;
  replays stay fog-filtered.
- **Debrief** is built from the fog-filtered replay, so it can't mention anything the viewer didn't see.
