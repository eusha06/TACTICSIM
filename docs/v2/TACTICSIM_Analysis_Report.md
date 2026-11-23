# TacticSim — Senior Game Dev Review & Redesign Report

**Repo:** github.com/eusha06/TACTICSIM (main @ `b12ba68`, "Release 5")
**Live:** tacticsim.onrender.com
**Reviewed:** 23 Nov 2026

---

## 1. Verdict in one paragraph

As **software**, TacticSim is in good shape. It has a clean C# rules engine, 81 xUnit tests, PostgreSQL saves with idempotent commands, CSRF protection and CI, and it deploys. As a **game**, it isn't working yet. The board is too small, the rules are written like a legal contract, the decisions are shallow, and the turn structure makes the opponent feel like a freeze followed by a jump. Don't throw the engineering away. Keep the C# server as the authoritative simulation, then rebuild the **game design** and the **presentation layer** on top of it.

---

## 2. What exists today

| Area | Current state |
|---|---|
| Stack | .NET 10, ASP.NET Razor Pages, EF Core + PostgreSQL (Prisma host), plain JS/CSS, SVG art, Docker on free Render |
| Board | Fixed 8×8 square grid with plain, forest (+1 def), hill (+2 def) and impassable water |
| Units | 3 vs 3: Infantry (10 HP, 3 move, range 1, 4 dmg), Armor (16 HP, 4 move, range 1, 6 dmg), Artillery (8 HP, 2 move, range 2–4, 5 dmg) |
| Combat | `damage = max(1, baseDamage − targetTerrainDefense)`. No unit-type matchups, no line of sight, no randomness |
| Turn model | I-go-you-go. You act, press **End Turn**, and the server runs the whole AI turn inside that one HTTP request |
| Objective | Hold F4 for "two completed human-plus-computer rounds"; leaving resets progress |
| AI | Greedy and deterministic: attack if possible, otherwise step toward the outpost or nearest enemy |
| Rendering | 64 DOM `<button>` cells plus SVG sprites; animations through the Web Animations API |
| Code size | Core about 600 lines of C#, web JS about 650 lines, 81 tests |

---

## 3. Why the rules are hard to understand (even for you)

1. **The objective is written like a legal clause.** "Keep a friendly unit on F4 through two completed human-plus-computer rounds. Losing occupancy resets the hold." A player needs to read this three times. Holding counts only *after the computer's turn*, which is invisible and feels like a bug ("I'm standing on it, why is it 0/2?").
2. **The "Turn" number counts half-turns.** `TurnNumber++` runs on every side switch, so "TURN 05" is really your 3rd turn. The capture rule talks about "rounds", which is a different unit. That's two counters with two meanings.
3. **Numbers aren't shown when you make a decision.** The game has no damage preview ("this attack does 4 → enemy goes 10 → 6"), no range overlay for artillery, and no enemy threat zones. You have to do arithmetic from a help table.
4. **Some rules are surprising and only visible in the docs.** Artillery can't hit adjacent units. Attacking locks movement but moving doesn't lock attacking. Terrain doesn't block line of sight, even across a river. Water blocks movement but not shooting.
5. **The units have no matchups.** Every unit deals flat damage to every other unit, so the only real decision is "attack whatever is in range", and that doesn't feel like tactics.
6. **The screen is crowded with text.** The objective banner, status bar, command post, unit intel, squad list, legend, terrain key, keyboard help, reminders and history all compete with a board that's only 8×8.

## 4. Why "my step stops, then the opponent goes"

This happens for three reasons that stack on top of each other:

- **Design: strict I-go-you-go.** Nothing moves while you plan. Then you press End Turn and everything is locked while the enemy acts. A *simulation* should feel like both sides act in the same world.
- **Tech: the whole AI turn runs in one blocking request.** `MatchService` calls `ComputerOpponent.PlayTurn()` inside the End Turn POST. That request also restores the game, writes events and receipts, and commits to PostgreSQL. While it runs, the board is disabled ("Opponent's turn…").
- **Presentation: the enemy's moves flash by.** `presentation.js` caps each step at **230 ms** and the whole sequence at about **2 s**, with a hard 2.2 s abort. Three enemy moves plus attacks happen in a blink. You see a pause and then the board "jumps".
- **Hosting adds a long first wait.** The free Render instance sleeps when idle. The README says it may take about a minute to wake, so the first action after a break feels frozen.

## 5. Strengths to keep

- Rules are server-authoritative in C#, with a clear Core/Web split. That's an excellent base for a deterministic simulation.
- The engineering is strong: idempotent commands, version checks, atomic saves, anonymous ownership, antiforgery, migrations and CI.
- The art direction (tabletop miniatures, blue vs orange) is pleasant and consistent. Keep the style and scale it up.
- Movement is deterministic, Dijkstra-based and well tested.
- **Career fit:** your Army War Game Center application asks for C#/.NET. A deterministic C# war-game *simulation engine* with an AI opponent is a much stronger portfolio piece for that role than a JS-only game. That's the main reason I recommend keeping the backend in C#.

## 6. Problems to fix (prioritized)

| # | Problem | Severity | Fix |
|---|---|---|---|
| 1 | No core tactical depth (flat damage, 3 units, tiny map) | Critical | New rules: unit matchups, cover, line of sight, fog of war, 3 objectives |
| 2 | Opponent turn feels like a freeze | Critical | **WEGO simultaneous turns** with an animated execution phase you can watch, pause and speed up |
| 3 | Confusing objective and turn counters | High | Victory points from 3 objectives, scored each turn, shown as a VP bar plus "Turn 3/12" |
| 4 | No decision previews | High | Path preview, combat preview card, line-of-sight line, threat overlay |
| 5 | Graphics limited by the DOM grid | High | WebGL canvas renderer (Phaser) with tweens, tracers, explosions, fog, camera |
| 6 | Text-heavy UI | Medium | Full-screen battlefield, compact HUD, icon-based rules cards, tooltips on every number |
| 7 | No real tutorial | Medium | "Boot Camp" scripted mini-mission that teaches by doing |
| 8 | Shallow AI | Medium | Utility-based order planner with fog-fair information and 3 difficulty levels |
| 9 | `AGENTS.md` forbids the redesign | Blocker for Claude Code | It bans fog of war, JS game engines, Node builds and frontend frameworks, and says "stop after Release 5". Must be superseded first |
| 10 | Cold start on free host | Low (cost) | Loading/"waking server" screen; optionally host the static client on GitHub/Cloudflare Pages |

---

## 7. Recommended new game: **TacticSim: Rivergate Command**

A small, readable, deterministic **WEGO tactical simulation** (simultaneous orders).

**Core loop (each turn, 12 turns max):**
1. **Plan:** give one order to each unit. There's no time pressure, and the enemy is planning at the same time.
2. **Execute:** both sides' orders resolve together over 8 "ticks", played as a 6–10 second animated battle (pause, 1×/2×/4×, skip, replay).
3. **Debrief:** a 3-line summary: objectives taken, units hit or lost, VP change.

**How you win (one sentence):** *Hold objectives to earn Victory Points. First to 12 VP, or destroy the enemy, or lead after Turn 12.*

**Map:** a 15×11 hex map with river, bridges, town, forest and hills. There are 3 objectives: **A** Bridge, **B** Comms Outpost and **C** Hill 112.

**Units (5 types, rock-paper-scissors):** Rifle Infantry, Anti-Tank Team, Armor, Artillery and Recon. Infantry are strong in towns and forests, armor dominates open ground, AT teams kill armor, artillery fires indirectly but is fragile, and recon sees far and spots for artillery.

**Orders:** Move, Attack (move then fire), Hold (dig in, +1 cover), Overwatch (fire at the first enemy that enters range during execution) and Barrage (artillery shells a hex at tick 6, so you're predicting where the enemy will be).

**Readability rules:** combat stays deterministic, so every preview is exact. Every number has a tooltip, every rule fits on one card with an icon, and the game shows the reason whenever something fails ("Blocked: forest at E5 breaks line of sight").

The full specification, with stat tables, tick-resolution rules, AI design, API, data model, art direction and acceptance criteria, is in `docs/v2/GAME_DESIGN_AND_ROADMAP.md`.

## 8. Target architecture

```
content/ (JSON: units, terrain, maps, scenarios)  ← single source of truth
   │
TacticSim.Sim (new, pure C#)  hex math · pathfinding · LOS/fog · tick resolver · scoring · AI
   │                                  │
TacticSim.Web (ASP.NET)  /api/v2/*    golden test vectors (JSON)
   │  Postgres: matches_v2, turn_orders_v2, turn_results_v2 (rules_version pinned)
   │                                  │
client/ (Vite + TypeScript + Phaser) → builds to wwwroot/game/
   preview mirror of rules (checked against C# vectors) · battle scene · HUD (DOM overlay)
```

- **Server is the authority.** The client previews paths, line of sight and damage for instant feedback, and the server re-validates everything. Golden test vectors exported from C# keep the TypeScript preview identical to the engine.
- **The server never leaks hidden information.** Fog-of-war filtering happens on the server, for both state and replays.
- **v1 is kept.** Old Rivergate saves stay readable and reviewable under "Classic", and no production data is deleted.

## 9. Delivery plan (summary)

| Phase | Outcome |
|---|---|
| 0 | Branch, supersede `AGENTS.md`, add `CLAUDE.md` + spec |
| 1 | *Optional quick wins on live v1:* plain-language objective, round counter, damage preview, slower watchable enemy turn |
| 2 | `TacticSim.Sim` engine plus tests (no UI) |
| 3 | AI opponent plus headless AI-vs-AI balance runner |
| 4 | API v2, persistence, migrations, security parity |
| 5 | Phaser client: map, planning UX, previews, execution playback, HUD |
| 6 | Art, effects, audio, Boot Camp tutorial, menus, accessibility, mobile |
| 7 | Docker/CI, deploy, verify, README, screenshots/GIF |

## 10. Risks

- **Scope:** this is a rebuild of the game layer. The phases are ordered so that each one ends in something testable, and Phase 1 improves the live game right away.
- **Simultaneous resolution edge cases** (collisions, mutual kills) are covered by explicit rules in the spec plus tests.
- **Free hosting** keeps its cold start. The client can be hosted statically to hide it.
- **Balance:** the headless AI-vs-AI runner gives you numbers instead of guesses.
