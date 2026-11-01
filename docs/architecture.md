# Architecture plan

The Core engine, Razor Pages interface, PostgreSQL persistence, and HTTP command boundary are implemented. See [rules.md](rules.md) for gameplay and [verification.md](verification.md) for automated evidence and the remaining manual browser checks.

## Dependency direction

```text
TacticSim.Persistence.Tests ---> TacticSim.Web ---> TacticSim.Core <--- TacticSim.Tests
      |
      +-- EF Core 10 / PostgreSQL
      +-- ASP.NET Core Razor Pages
```

Core should feel like a small C++ library: plain C# types, explicit state, and functions that enforce rules. Web translates HTTP requests into Core operations and persists accepted changes. Keep EF entities and request/response types in Web so serialization and database concerns do not dictate the domain design.

## Engine phase

`Position`, `BattleMap`, `Side`, abstract `Unit` and its three subclasses model the domain. `Game.CreateDefault()` supplies the fixed 8x8 map and three units per side. `Game` copies supplied units and exposes read-only views to prevent accidental state sharing between matches. Unit statistics, terrain costs, ranges, deterministic damage, and action allowances are documented in the rules and covered by tests.

`Game.Move`, `Game.Attack`, and `Game.EndTurn` validate changes and return `ActionResult` with an explicit `GameError`. They check turn, unit ownership, bounds, obstacles, movement cost, attack range, health, and match completion before mutating state. Legal-move queries and movement commands use the same Dijkstra search. Legal-target queries and attacks use the same attack validator. Rejected commands leave state unchanged.

`ComputerOpponent.PlayTurn` uses those same queries and commands. It processes units in ID order, fires when possible, otherwise picks a legal destination using a deterministic distance heuristic, then attacks and ends its turn. Tie breakers are explicit. Its greedy movement can wait behind obstacles; it does not plan several turns ahead.

The xUnit suite covers legal and illegal actions, terrain routes, occupied cells, turn resets, defeated units, victory, state isolation, and deterministic computer behavior. Core has no EF or HTTP dependencies.

## Web and persistence phase

The browser sends intent: a unit ID, destination or target, command ID, and expected match version. Selection is local UI state. The server returns authoritative state and legal choices for highlighting; the browser never computes legal movement or damage.

`IndexModel` exposes `GET /?handler=State` and `POST /?handler=Command`. The page itself establishes the anonymous owner cookie and renders an antiforgery token. State GETs do not create or mutate matches. JavaScript starts a missing match with a POST. All state responses are private/no-store.

For each command:

1. Validate the antiforgery token. Resolve an opaque, server-recognized anonymous session from its cookie. Generate session identity on the server and use appropriate HttpOnly, SameSite, and production Secure cookie settings.
2. Load the match scoped to that owner. Apply this ownership check to every read and mutation, including resume, history, and results. A guessed match ID grants no access. Do not accept an owner ID from the request body.
3. Check whether this owner already has a receipt for the command ID. An identical retry returns its prior outcome without running the engine again. Reusing the ID with a different payload is rejected.
4. Compare the expected version with the persisted match version. Reject stale state and return an appropriate conflict response. Pass only an authorized operation into Core.
5. Persist the new state, appended action history, result if completed, incremented version, and command receipt in one PostgreSQL transaction. Use an optimistic concurrency condition on the version and a unique `(OwnerId, CommandId)` primary key. Handle concurrent retries and roll back failed writes. Process-local locks do not protect multiple server instances.

`MatchService` reconstructs an isolated `Game` from a plain `GameSnapshot`. End Turn includes the human turn change and the entire deterministic computer turn in one transaction. Core records accepted `GameAction` values, including actual damage and destruction; Web formats them into the saved action history. Every successful command increments the session revision; gameplay and Resume also increment that match’s separate version. New matches start at match version 1. The request carries the expected active match ID, match version, and session revision, preventing old tabs from applying actions after New Match or Resume. A retry of the same command returns its stored result without applying actions again. Reusing an ID with a different known payload is rejected. The frontend ignores older versions and keeps the same command ID after an interrupted connection.

Razor Pages applies antiforgery validation to the JSON POST handler. Fetch sends the page token in `X-CSRF-TOKEN`. `BrowserOwner` uses ASP.NET Data Protection for a tamper-resistant owner cookie (HttpOnly, SameSite Strict, Secure over HTTPS). No endpoint accepts a match ID or owner ID to select another visitor’s match. Integration checks cover independent sessions, missing/invalid CSRF tokens, forged cookies, repeated command IDs, stale versions, and actual concurrent PostgreSQL writes.

`MatchDbContext` uses relational tables:

| Table | Stored data |
| --- | --- |
| `GameSessions` | Protected-cookie owner ID, active match ID, session revision concurrency token |
| `GameMatches` | Match ID/owner, version concurrency token, fixed map key, side/turn, winner, timestamps, last event sequence |
| `MatchUnits` | Composite match/unit ID, unit type/side, coordinates, health, move/attack flags |
| `MatchEvents` | Composite match/sequence key, command link, turn/side/kind, unit/target/destination, actual damage/destruction, display text and timestamp |
| `MatchCommands` | Unique owner/command ID, match, accepted intent, expected/resulting match versions, fingerprint and cached HTTP response |

Game restore reads the match and unit rows; action history reads event rows ordered by sequence. The one fixed map is identified by immutable `crossing-v1`, with terrain supplied by Core. It is not duplicated as a JSON blob. The JSON response on a command row is only a retry cache; no live game is reconstructed from it.

`SaveChanges` commits session selection, match/unit updates, ordered events, and the command receipt in one transaction. PostgreSQL primary keys/foreign keys enforce associations; concurrency tokens arbitrate races. Events use an incrementing sequence assigned on the match under that version check, so accepted events cannot acquire duplicate sequence numbers. New Match preserves earlier operations; Resume selects an owned unfinished match, increments its version, and appends a resume event without resetting its units. Completed results are stored on match rows.

`/History` lists only the cookie owner's matches. Its optional `matchId` review is scoped by owner before returning details. Resume is a Razor POST form with antiforgery validation and the same version/idempotency checks as board commands. An unknown or another owner's review/resume target returns 404. GET requests never change active match selection.

The initial EF migration creates the schema and transactionally imports the previous MVP's snapshot/receipt tables when present. It recovers the latest cached version of each old match, splits units/history into rows, and preserves receipt IDs. It refuses an incomplete legacy conversion rather than silently discarding state. Old tables remain as unused backups. Historical text events are labeled `Legacy` because structured command links/damage metadata were not recorded; new events have these fields. Imported start dates use completion/import time, since original start times were not saved. Fresh databases simply receive the new tables. Apply migrations explicitly with `dotnet ef database update` or `--migrate`; there is no `EnsureCreated` path and no reseeding.

Action history remains a readable log, not event-sourced state or a replay. Connection credentials belong in user secrets/environment variables. Browser identity uses the existing ASP.NET Data Protection purpose and cookie, so schema conversion does not change cookie identity. Local key-ring persistence was verified across a real process restart; a deployment must retain its key ring as well as PostgreSQL data.

## UI phase

The responsive 8x8 board uses plain CSS/JavaScript. It renders server-provided legal move costs and target IDs. Selection is local presentation state; even clicks on non-highlighted cells are validated by the server and produce readable errors. Controls are disabled while a request is pending. On conflicts the server supplies current state; network retries reuse the pending command. Unit labels, HP, selection details, terrain legend, turn indicator, history, and completion banners are rendered without external assets or client combat rules. Keyboard arrows navigate cells and Escape clears selection.

No frontend framework, Node build pipeline, login, multiplayer, fog of war, external AI API, or replay system is part of the MVP.
