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
- **AI input is a `MatchView`** (the same fog-filtered view a player gets), so the planner can't cheat by
  construction. Last-seen markers become pseudo-units, except markers on hexes the AI can see right now.
- **Veteran is not fully deterministic across seeds:** it picks with the match's seeded RNG among candidates
  within 0.5 points of the best. Same seed + same view = same plan. Without this, 500 mirrored games would
  be one game played 500 times.
- **Commander's "top 5 plans"** are the greedy plan plus 4 variants that force the 2nd-best choice for the
  first 4 assigned units. The 3 "likely responses" are all-Hold, all-Overwatch, and a Veteran plan made from
  the enemy's view of what Commander knows. Plans score 50% worst case + 50% average.
- **Boot Camp's opponent** follows `"redScript": "hold"` in the map file: every Red unit Holds.
- **The visitor always plays Blue.** Red's orders are planned on the server inside the turn request, from
  Red's own fog-filtered view, then validated like player orders (an invalid AI order set falls back to Hold).
- **Current state** = the latest `turn_results_v2.end_snapshot`, or the scenario rebuilt from `ai_seed` before
  turn 1. `turn_orders_v2` gained `owner_id`, so "command id unique per owner" is a plain unique index.
- **Idempotent retry** returns the saved outcome of the turn that command played (its replay and end state).
- **API JSON** uses camelCase with string enums (`"move"`, `"blue"`). `/api/v2/rules` is public; everything
  else needs the owner cookie, and POSTs need the `X-CSRF-TOKEN` header from the page's antiforgery token.
- **Art pipeline:** original SVG lives in code (`client/src/art.ts`) and is rasterized into Phaser textures at
  boot (2x for crisp zoom). No build-time atlas: ~50 small textures load instantly.
  ponytail: add an atlas pack step if texture count or load time grows.
- **Client session:** `GET /api/v2/session` sets the owner cookie and returns the antiforgery request token, so the
  canvas app (and `npm run dev` through Vite's proxy) can POST with `X-CSRF-TOKEN`.
- **Replays from the after-action report** rebuild each turn's starting positions from tick 1 (`from` of the first
  move, plus damage taken on tick 1). Objective flags in that replay show the final state.
- **Reduced motion** (`prefers-reduced-motion`): no tweens or particles; the turn result appears at once with the
  full written event log.
