# TacticSim v2 — "Rivergate Command"
## Game Design Spec + Implementation Roadmap (source of truth for Claude Code)

> Status: authorized redesign. This document supersedes the scope limits in `AGENTS.md`
> (Releases 0–5). Where this doc and `AGENTS.md` conflict, **this doc wins**. Security,
> data-safety and deployment-safety rules from `AGENTS.md` still apply.

---

## Part A — Design goals

1. **Easy to understand.** A new player wins Boot Camp without opening the rules. Every rule fits on one icon card. Every number on screen has a tooltip. Every rejected action says *why*.
2. **Feels like a simulation.** Both sides act at the same time. You watch the battle unfold instead of waiting through an opponent turn.
3. **Good graphics.** An animated tabletop-diorama battlefield (WebGL canvas) with smooth movement, tracers, shell arcs, explosions, smoke, fog of war and a camera.
4. **Deterministic and fair.** There's no dice: previews are exact, and same orders give the same result. The AI sees only what its units see.
5. **Engineering stays strong.** The C# server is authoritative, and the project keeps its tests, idempotent saves and CI.

## Part B — Rules (player-facing summary, put this on the Rules screen as cards)

**How to win:** Hold objectives to earn **Victory Points (VP)**. The first side to **12 VP** wins. You also win by destroying every enemy unit, or by leading on VP after **Turn 12** (equal VP is a draw).

**Each turn:**
1. **Plan:** give each of your units one order. The enemy plans at the same time, and you can't see their orders.
2. **Execute:** all orders play out together over 8 ticks. Watch it, pause it, or speed it up.
3. **Score:** at the end of the turn, each objective you **control** gives you +1 VP.

**Control:** you control an objective if one of your units is on it and no enemy unit is. If both sides have a unit on or adjacent to it, it's **contested**. Contested means nobody controls it.

**Contact:** a moving unit stops when it enters a hex next to a *visible* enemy.

**Seeing:** you only see hexes your units can see (fog of war). Forests, towns and hills block line of sight, and units on hills see over forests. Enemies you saw before stay on the map as faded "last seen" markers.

**Damage:** `damage = attack value (vs Foot or vs Vehicle) − cover of target hex`, minimum 1. Hold gives +1 cover. No randomness.

## Part C — Content tables (store in `content/*.json`, never hard-code in C# or TS)

### C1. Terrain (`content/terrain.json`)

Move costs are in **ticks**. A unit can spend up to its Move value in ticks.

| Terrain | Foot cost | Vehicle cost | Cover | Blocks LOS | Notes |
|---|---|---|---|---|---|
| Open | 1 | 1 | 0 | no | |
| Road | 1 | 1 | 0 | no | Vehicles moving road→road get +2 Move that turn if the **whole** path is road (visible "road march" badge) |
| Forest | 2 | 3 | 2 | yes | Can see into an adjacent forest hex, not through it |
| Hill | 2 | 2 | 1 | yes* | *Blocks LOS between two lower hexes. Units on hills ignore forest/town LOS blocking and get +1 Vision |
| Town | 1 | 2 | 3 | yes | Best cover for Foot units |
| River | — | — | — | no | Impassable |
| Bridge | 1 | 1 | 0 | no | Only river crossing |

### C2. Units (`content/units.json`)

| Unit | Class | HP | Move | Range | Atk vs Foot | Atk vs Vehicle | Vision | Special |
|---|---|---|---|---|---|---|---|---|
| Rifle Infantry | Foot | 10 | 4 | 1–2 | 4 | 2 | 3 | Captures. +1 cover in Town/Forest (stacks) |
| Anti-Tank Team | Foot | 8 | 3 | 1–3 | 2 | 7 | 3 | Captures |
| Armor | Vehicle | 18 | 5 | 1–3 | 6 | 5 | 3 | Captures. −1 cover in Forest/Town (vulnerable in close terrain) |
| Artillery | Vehicle | 9 | 3 | 3–6 | 6 | 4 | 2 | Indirect fire: can target any hex any friendly unit sees. Cannot move and fire in the same turn. Can't capture |
| Recon | Vehicle | 8 | 7 | 1–2 | 2 | 1 | 5 | Spots for artillery. Can't capture |

Starting forces for Rivergate (per side): 2 Rifle Infantry, 1 Anti-Tank, 1 Armor, 1 Artillery, 1 Recon.
All values are first-pass. Tune them with the Phase 3 balance runner, and record changes in `docs/v2/balance-log.md`.

### C3. Map (`content/maps/rivergate.json`)

- 15 columns × 11 rows, **pointy-top hexes, odd-r offset** storage, axial/cube math internally.
- A river runs north–south through the middle, with 2 bridges (north and south).
- Objectives: **A** = north Bridge, **B** = Comms Outpost in the central-east town, **C** = Hill 112 on the west bank.
- Blue deploys on the west edge, Red on the east edge. The map should be mirror-ish but not identical. Each side has one objective closer to it, and B is central.
- Map JSON: `{ id, name, width, height, tiles: [string rows using single-char codes], objectives: [{id,label,col,row}], deployments: {blue:[...], red:[...]}, turnLimit: 12, vpToWin: 12 }`.
- Also author `content/maps/bootcamp.json`, a 9×7 tutorial map with 1 objective and 2 vs 2 units.

## Part D — Orders and simultaneous resolution (the core simulation)

### D1. Orders (exactly one per unit per turn; default = Hold)

| Order | Input | Effect |
|---|---|---|
| **Move** | path to a hex | Moves along the path. At end of movement, fires at its assigned target if set, otherwise "fire at will" (best visible target in range) |
| **Attack** | target unit (+ optional path) | Moves along the path (if any), then fires at that target the first tick it's in range and LOS. If never in range, does nothing else |
| **Hold** | — | Stays. +1 cover this turn. Fires at will at end of turn |
| **Overwatch** | — | Stays. Fires at the **first** enemy that enters range+LOS during ticks 1–8 (tick-accurate). If none, no shot |
| **Barrage** | target hex (artillery only, unit must not move) | A shell lands at **tick 6**. It damages whatever unit is on that hex at tick 6 (either side; friendly fire is possible and warned in the preview). The target hex must be in range and seen by some friendly unit at the **start** of the turn |

Each unit fires **at most once** per turn (Barrage counts as its shot).

### D2. Tick resolution (8 ticks), in this exact order each tick

1. **Movement step.** Every unit with remaining path tries to enter its next hex if it has enough remaining ticks for that hex's cost. A cost-2 hex takes two ticks: the unit is "in transit" on the first tick and arrives on the second.
2. **Collisions:**
   - Two units entering the same hex on the same tick: both stay where they were and their paths end. A "blocked" event is shown.
   - Entering a hex occupied by an enemy (visible or not): stop, path ends, and the enemy becomes visible to that unit.
   - Friendly units may pass through each other but may not end in the same hex. If the final hex is taken, stop at the last free hex.
3. **Contact:** if a unit's new hex is adjacent to an enemy it can now see, its path ends.
4. **Vision update** for both sides (LOS from every unit, Vision radius).
5. **Fire:** collect every shot that becomes valid this tick (Overwatch triggers, Attack targets now in range, Barrage at tick 6). Then **apply all damage at once**, so units destroyed this tick still fire this tick (mutual kills are possible).
6. **Fire at will** happens after tick 8, for units that haven't fired: Move units that ended movement and Hold units pick the target with the highest damage dealt, then lowest remaining HP, then lowest unit id.
7. **End of turn:** remove destroyed units (they leave a wreck decal, which is cosmetic only). Evaluate objective control and add VP. Check win conditions in this order: annihilation, VP ≥ 12, turn limit. If both sides reach 12 VP on the same turn, the higher VP wins and a tie is a draw.

All iteration orders must be deterministic (sort by unit id). Tests must cover every collision and contact case.

### D3. Line of sight (hex)

- Use the cube-coordinate line drawn between hex centers, with a tiny epsilon nudge so lines along hex edges always resolve the same way.
- Blocking hexes are those **strictly between** observer and target. A blocker blocks if its terrain blocks LOS (with the hill exceptions from C1).
- Artillery indirect fire ignores its own LOS but needs a target hex that *any* friendly unit currently sees.

## Part E — AI opponent (`TacticSim.Sim/Ai`)

- **Information:** the AI uses only its own side's fog-filtered view plus last-seen markers. It must not cheat.
- **Planner:** utility scoring. For each unit, generate candidates: reachable hexes × order types, pruned to the top ~30. Score each candidate with:
  - `+ objective value` (uncontrolled or enemy-held objectives are worth more; staying on a held objective is worth more still)
  - `+ expected damage dealt` (using the exact damage formula against visible or last-seen enemies)
  - `− expected damage received` (threat map from known enemy ranges)
  - `+ cover`, `+ overwatch value on likely approach hexes`, `− contact risk for Artillery and Recon`
- **Coordination:** assign units greedily in order of best score, reserve destination hexes, and avoid two units on the same target when one is enough to kill it.
- **Difficulty:**
  - **Recruit:** picks randomly among the top 3 (seeded RNG stored with the match), ignores threat.
  - **Veteran:** takes the best score.
  - **Commander:** takes the best score, plus it re-scores its top 5 plans against the player's 3 most threatening likely responses.
- **Performance:** under 150 ms per turn on Render free. Add a test that enforces the budget loosely.
- **Balance runner:** `dotnet run --project tools/TacticSim.Balance -- --games 500 --red Veteran --blue Veteran`. It outputs win rate per side, average turns, VP curve, unit kill/loss rates and objective hold %. Target a 45–55% win rate for mirrored Veteran AIs.

## Part F — Server, API, persistence

### F1. Projects

- `src/TacticSim.Sim/`: **new**, pure C#, with no ASP.NET or EF references. It contains Hex math, Map/Content loader, Pathfinding (Dijkstra/A* on hex with tick costs), Visibility, Orders validation, `TurnResolver` (returns `TurnResult { ticks: [TickEvents], finalState, vpDelta, debrief }`), Scoring and Ai.
- `src/TacticSim.Core/`: **keep unchanged** so v1 saves still load and review.
- `tests/TacticSim.Sim.Tests/`: engine tests. Also emits golden vectors to `client/test/vectors/*.json` (paths, LOS cases, damage cases, a full resolved turn).
- `tools/TacticSim.Balance/`: console app for AI-vs-AI runs.

### F2. API (`/api/v2`, JSON, antiforgery header + owner cookie like v1)

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/v2/rules` | Content manifest (units, terrain, map list) plus `rulesVersion` |
| POST | `/api/v2/matches` | Create `{scenario:"rivergate"|"bootcamp", difficulty}` → state |
| GET | `/api/v2/matches/{id}` | **Fog-filtered** state for the owner |
| POST | `/api/v2/matches/{id}/turns` | `{commandId, expectedVersion, orders:[...]}` → `{replay, state}`. Idempotent: same commandId returns the saved result |
| GET | `/api/v2/matches/{id}/turns/{n}` | Fog-filtered replay of a past turn |
| GET | `/api/v2/matches` | Owner's match list (v2 plus "Classic" v1, read-only) |

- The server validates every order (ownership, legal path, range, Barrage rules). Invalid orders are rejected with a reason string the UI shows verbatim.
- **Never leak hidden info.** Enemy units the player can't see are omitted from state and from replay events, and so are enemy orders. Test this explicitly.

### F3. Persistence (EF migration, additive only)

- `matches_v2` (id, owner_id, scenario, difficulty, rules_version, ai_seed, turn, status, vp_blue, vp_red, version, created_at, updated_at, completed_at)
- `turn_orders_v2` (match_id, turn, side, orders jsonb, command_id unique per owner)
- `turn_results_v2` (match_id, turn, replay jsonb, end_snapshot jsonb)
- Pin `rules_version` per match. Content files are versioned (`content/v1/...`) so a balance change never alters an in-progress save.
- Determinism check: a test re-runs a saved match from scenario + orders and must reproduce every snapshot.
- Do not drop or modify v1 tables.

## Part G — Client (graphics and UX)

### G1. Stack

- `client/`: Vite + TypeScript (strict) + **Phaser** (current stable; check npm) + Vitest. Builds to `src/TacticSim.Web/wwwroot/game/`.
- Razor page `/play` hosts the canvas plus a **DOM overlay HUD** (HTML/CSS for crisp text, tooltips and accessibility). The battlefield is canvas.
- `client/src/rules/`: a TypeScript mirror of pathfinding, LOS and damage **for preview only**. It must pass the C# golden vectors. The server result is always what's shown after Execute.
- Scenes: `Boot` (loading, plus a "waking server" message if `/api/v2/rules` takes >2 s), `Menu`, `Battle`, `Debrief`.

### G2. Art direction: "tabletop diorama"

- Keep the existing look: muted olive and greens, blue (#4b8fd6-ish) vs orange (#e0813a-ish) sides, and miniature figures on round bases. Use original art only.
- Draw hex tiles as original SVG in `client/art/`, then pack them into a texture atlas at build time. Give each terrain 2–3 variants so the map doesn't tile visibly, and use soft drop shadows.
- Units are side-colored base rings with a **shape marker** for colorblind safety (circle for Blue, diamond for Red), a silhouette sprite that rotates or faces its movement direction, an HP pip bar and an order icon.
- **Effects:** tracer lines plus muzzle flash for direct fire; a parabolic shell arc with a whistle → explosion (particles + scorch decal) for artillery; floating damage numbers; smoke plus a wreck decal on destroyed units; subtle water shimmer and tree sway; a soft-edged, desaturated fog overlay; a flag-raise animation on objective capture.
- **Camera:** drag-pan, wheel/pinch zoom, double-click to center, an "auto-follow action" toggle during execution, and a minimap in the corner.
- **Audio:** small SFX set (select, order, shot, cannon, explosion, capture, victory), synthesized with Web Audio or original assets, plus a master volume and mute toggle.

### G3. Planning UX (this is where "easy to understand" is won)

- **Top bar:** `TURN 3 / 12` · `PLANNING` phase badge · VP bar `BLUE 5 ■■■■■□□ RED 3` (target 12) · objective chips A/B/C colored by controller.
- Click a unit to see its reachable hexes, tinted by tick cost. **Hover a hex** to show the path arrow, its tick count, a "Contact likely" warning if it ends next to a known enemy, and a "Road march +2" badge when that applies.
- **Hover an enemy** to show the combat preview card: `Your Armor → Enemy Rifle Infantry (Forest, cover 2): 6 − 2 = 4 dmg · HP 10 → 6`, plus a LOS line that is green when clear and red when blocked, with the blocking hex highlighted and named.
- **Threat overlay (T key / button):** red-hatched hexes that known enemies can hit next turn.
- **Order list panel:** one row per unit with its order icon and a ✓. The big button reads `EXECUTE TURN ▶ (5/6 ordered)`, and units without orders default to Hold (it says so).
- Undo/redo for orders before executing.
- **Plain-English errors:** "Artillery can't fire after moving" or "Out of range: needs 3–6, target is 2."

### G4. Execution playback

- About 600 ms per tick at 1×, for a 5–8 s battle. Controls: ⏸, 1×/2×/4×, Skip, and Replay last turn.
- A narrative ticker line shows events like "Tick 3 — Red Armor spotted at H5", "Blue AT Team fires: 7 damage".
- Input is locked during playback. Reduced motion shows the final state instantly, with the full event log.

### G5. Debrief (after each turn)

- 3–5 plain lines: VP gained, objectives changed, units hit or destroyed, newly spotted enemies. Button: **Next Turn**.
- End of match: an after-action report with the VP graph per turn, kills/losses, turns taken, and a replay of any turn.

### G6. Onboarding

- **Boot Camp** (first launch): 3 scripted turns on `bootcamp.json`, each with one highlighted instruction: (1) select and move, (2) read the preview and attack, (3) take the objective and press Execute. It can be skipped and replayed from the menu.
- **Rules** screen: 8 icon cards (Win, Turn, Orders, Move & Terrain, Seeing, Damage, Objectives, Units), each ≤ 3 lines.
- Contextual tips appear the first time something happens ("Your unit stopped: Contact with an enemy").

### G7. Accessibility and devices

- Keyboard: Tab/Shift-Tab cycles units, arrows move a hex cursor, Enter confirms, Esc cancels, Space executes, T toggles threat, 1–5 pick the order type.
- Colorblind-safe via shape markers. Text contrast meets AA. Respect `prefers-reduced-motion`.
- Desktop first. Tablet/mobile get touch pan/zoom, tap-to-select and a bottom-sheet HUD at ≥ 360 px width.

## Part H — Roadmap (do phases in order; each ends green, playable or tested, and committed)

Work on branch `v2-simulation`. Don't deploy until Phase 7. Commit after each logical step with clear messages.

### Phase 0 — Guardrails
- Create branch `v2-simulation`.
- Add a "Release 6 / v2 authorization" section at the **top** of `AGENTS.md` pointing to this doc and lifting the bans on fog of war, a JS game engine (Phaser), a Node/Vite build and new rules for v2. Keep all security, data-safety and deploy-safety rules.
- Record the plan in `docs/release-checklist.md`.
- **Done when:** branch exists, docs updated, existing build and tests still green.

### Phase 1 — Quick wins on live v1 (small, optional, can ship to main before v2)
- Objective text becomes: "Stand on F4 at the end of 2 enemy turns in a row." Show `Round N` (full rounds) instead of the half-turn counter.
- Hover an attackable enemy to see the damage preview; show the selected artillery's range ring.
- Slower, watchable enemy turn: ≥ 600 ms per step, an "ENEMY TURN" banner, pause/speed/skip, and no 2.2 s hard cap. Keep the skip and reduced-motion paths.
- Pulse End Turn when all units are spent.
- **Done when:** existing tests pass, JS tests updated, manual playthrough OK.

### Phase 2 — Simulation engine (`TacticSim.Sim`) and tests
- Hex math, content loader with schema validation, pathfinding with tick costs and road march, LOS and vision, order validation, `TurnResolver` per Part D, scoring and win checks, debrief text.
- Tests: every terrain cost, LOS blockers and hill rules, all collision and contact cases, mutual kill, Overwatch trigger tick, Barrage hit/miss/friendly fire, VP and contested control, every win condition, determinism (same input → same output), and save → reload → identical.
- Emit golden vectors for the client.
- **Done when:** ≥ 90% line coverage on Sim, and all tests green.

### Phase 3 — AI and balance
- Utility AI with 3 difficulties per Part E, fog-fair.
- `tools/TacticSim.Balance` runner. Tune `content` until mirrored Veteran is 45–55%, average game ≥ 7 turns, and every unit type gets used. Log results in `docs/v2/balance-log.md`.
- **Done when:** the balance targets are met and the AI performance test passes.

### Phase 4 — API and persistence
- Endpoints per F2, migration per F3, owner binding, antiforgery, idempotent commandId, version conflicts (409), fog filtering.
- Tests in `TacticSim.Persistence.Tests`: ownership, CSRF, idempotent retry, concurrent submit, fog leak tests, determinism replay, and v1 matches still load.
- **Done when:** all persistence tests are green against PostgreSQL.

### Phase 5 — Client: playable battle
- Vite/TS/Phaser scaffold, atlas pipeline, map rendering, camera, unit sprites, selection, all planning UX from G3, execution playback from G4, debrief from G5, menu, and `/play` page wiring. Vitest passes the golden vectors.
- **Done when:** a full Rivergate match is playable end-to-end locally against Veteran AI.

### Phase 6 — Polish: graphics, audio, onboarding, accessibility
- All effects from G2, audio, minimap, Boot Camp from G6, Rules cards, tips, keyboard and touch from G7, loading/"waking" screen, and the after-action report.
- Playtest checklist (record results): a new player finishes Boot Camp in < 3 min without reading rules; no unexplained rejection messages; 60 fps on a mid laptop at 1080p; no console errors.
- **Done when:** the checklist passes.

### Phase 7 — Ship
- Dockerfile: add a Node build stage for `client/` (`npm ci && npm run build`) before `dotnet publish`.
- CI: add `npm ci`, typecheck, Vitest, client build, and the Sim tests.
- Home page links "Play Rivergate Command" (v2) and "Classic" (v1 history).
- README rewrite with screenshots and a GIF of an execution phase; update `docs/portfolio.md`.
- Back up the production DB, run the migration, deploy to Render, verify `/health` revision, play a full match on the live URL, and check an old v1 save still opens.
- **Done when:** live URL verified and the checklist is updated with the commit SHA.

## Part I — Non-negotiables (carry over from v1)

- The C# server is the authority. Client logic is for preview only.
- No production data deletion, no `EnsureCreated`, migrations only, backup before migrating.
- Secrets stay out of Git, and must never be printed.
- Never claim a test, browser check or deployment passed without observing it.
- Original art only: no copyrighted or real-world insignia, and no real military unit names. The setting is fictional.
