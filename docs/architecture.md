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

`IndexModel` exposes `GET /?handler=State` and `POST /?handler=Command`. The page itself establishes the anonymous owner cookie and renders an antiforgery token. State GETs do not create or mutate matches. Only the explicit Begin Operation or confirmed New Match control starts a match with a protected POST. Opening or refreshing the page never starts one. The state read also returns an owned unfinished Continue candidate: active unfinished first, otherwise the most recent unfinished match. Continue opens the active save without a write, or uses the same protected Resume command for an older one. All state responses are private/no-store.

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
| `GameMatches` | Match ID/owner, version concurrency token, fixed map key, side/turn, winner, mission/objective/hold/evaluation state, timestamps, last event sequence |
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

Release 1 replaces abstract glyphs with an original local SVG sprite. Razor versions
its URL alongside CSS/JavaScript. The renderer uses only server tile/occupancy data;
neighboring water cells influence shoreline artwork, never legal actions. Health
bar widths present server health. Distinct silhouettes, native buttons, accessible
cell labels, keyboard focus restoration and reduced-motion CSS support different
input modes. History and help use native disclosure controls. No schema migration
or server contract change is needed. See [artwork.md](artwork.md).

No frontend framework, Node build pipeline, login, multiplayer, fog of war, external AI API, or replay system is part of the MVP.

## Release 2 briefing and tutorial

The initial view is a briefing. A tab remembers only which match it was viewing
in sessionStorage, so refresh can restore the board without sending a command.
History's successful Resume redirects with `play=1`, a presentation hint that
opens the server-owned current state. It cannot select or grant access to a match.
How to Play is a native dialog available before and during play. Razor derives
unit health/budgets/ranges, per-terrain movement costs and defense from Core.
Contextual range errors are formatted in Web only after the engine rejects an
attack; JavaScript never computes legality or distance to produce those errors.

The small `Tutorial` class tracks selection → successful move → explanation.
Only a living friendly selection advances step one. Only a successful matching
server response for the armed unit/match/destination advances step two. Rejected
commands, old spent flags, selection changes and timers cannot stand in for a
move. Identical retry receipts advance at most once. The final explanation is
acknowledged with Got it. The tutorial never gates gameplay controls.

localStorage holds optional per-match tutorial progress and dismissal preferences,
not game state or ownership. Storage failures affect guidance persistence only.
Skip and Replay have no network writes and never change command IDs, pending
retries, units or saves. Replay on a spent unit explains its actual server flags
and leaves End Turn available. `scripts/tutorial.test.cjs` runs using Node's
built-in test runner without npm or a frontend build; HTTP tests cover read-only
briefing/Continue discovery and the unchanged command/security boundary.

## Release 0 hosting boundary

The multi-stage .NET 10 image runs Web as a non-root user at stable content root
`/app/`, honors the host's `PORT`, and requires
`ConnectionStrings__DefaultConnection`. Production Data Protection persists its
key ring in PostgreSQL's additive `DataProtectionKeys` table; Development retains
the original local ring. Owner cookie name/purpose and gameplay state are
unchanged. A one-time `--import-keys` operator command can add an older filesystem
ring without deleting keys; the old application discriminator must also remain
the same. Keys and game tables must be backed up together.

`ReverseProxy__Enabled` explicitly trusts a managed ingress's forwarded HTTPS
scheme before redirection/cookie creation. No forwarded host or client identity
is accepted. `/health` reads required tables and exposes only readiness/revision,
including 503 during database failure; it creates no owners, keys, or matches.
Normal .NET application startup never applies migrations. The Docker image's
versioned `Deployment/start-web.sh` explicitly runs `--migrate` and starts the server only after success,
after the operator's review/backup gates. For transaction-pooled hosts such as
Prisma Postgres, optional `ConnectionStrings__MigrationConnection` is selected
only by `--migrate` to keep the migration lock on a direct session. Runtime and
Data Protection keep using `ConnectionStrings__DefaultConnection`; existing
one-string configurations remain compatible. No production database files are
stored in the web container. See [deployment.md](deployment.md) for the host's
trust assumptions, account steps, migration/rollback procedure, and evidence
limits. The Blueprint is deployment preparation, not evidence of a hosted app.

## Release 3 mission compatibility

`MissionType` is an explicit Core enum. Optional appended snapshot fields default
to Elimination/null/zero, preserving old JSON and callers. `CreateRivergate()`
opts new browser matches into capture without changing `crossing-v1`. Restore
validates progress and computes victory from mission state and living units.
Only `EndTurn(Computer)` evaluates capture, guarded by the persisted completed
computer-turn number. Move/attack immediately reset lost occupancy. Events record
hold/reset/completion/failure and nullable progress; history is still not a replay.

The additive `20261101191642_RivergateMission` migration adds five match columns
and nullable event progress. `Mission` defaults to `Elimination`, coordinates to
null, counters to zero. Thus both previously relational games and old snapshots
imported by InitialRelational retain elimination. No rows are deleted/reseeded.
Old response receipts omit the new optional mission DTO and remain replayable;
canonical state still comes from relational rows, never the receipt JSON.
Mission fields, units, ordered events, versions and receipt commit together.

The browser renders mission coordinates, counts, instructions and conclusions
from C#. It draws an original SVG outpost only on the server-supplied objective,
with a smaller marker when occupied. Briefing/help/tutorial/history distinguish
legacy elimination saves from new capture matches. No browser capture rules.

## Release 4 command presentation

The Dijkstra search now retains predecessor cells. Both `GetLegalRoute` and
accepted Move events trace that same shortest route, including origin and
endpoint. Movement cost, blocking, budgets, tie handling and opponent choices
are unchanged. Each successful Core move/attack/turn captures a copied snapshot
after all related reset/victory logic. Related history entries share that actual
post-command frame; later opponent commands cannot change an earlier frame.

Web returns an optional `presentation` beside command `state`: command/match IDs,
base/result match versions, ordered event sequence/turn/text, unit/target IDs,
actual damage/destruction, route, and complete server-produced display frames.
Intermediate legal-action arrays are empty because commands are locked. The
normal final state contains the authoritative legal choices. Frames include turn,
unit health/actions/positions and mission instructions/progress/outcome from C#.

No schema migration: canonical relational match/unit/event rows remain unchanged.
The optional presentation is cached as an extra property in the existing flat
receipt JSON, atomically with those rows. Retry returns the exact same sequence;
old receipt JSON lacks that property and remains compatible. Read/refresh,
History and Resume return saved state without replaying earlier actions. This
short command presentation does not turn action history into a replay feature.

`TurnPresentation` checks command/match/version/event continuity, orders frames,
and caps the sequence at roughly two seconds. The renderer moves a visual clone
through supplied waypoints, animates health to supplied values and uses supplied
damage/destruction. It never calculates a route, damage or intermediate game
state. `state` remains the saved final response while `display` temporarily uses
server frames. Controls stay locked through saving/presentation. Skip/Escape,
reduced motion, resize, hidden tabs and effect failures settle on the final
saved state; a hard deadline also releases a stalled effect. Browser refresh
reads the already committed state, never resubmits an animation. Failed network
requests retain the original pending command ID for safe retry.

## Release 5 presentation boundary

The read-only `MissionReport` derives outcome, completed rounds, surviving unit
counts and capture result from restored Core state. Completed rounds are
`(TurnNumber - 1) / 2` for the fixed human-first scenario. A victory reached at a
computer-round boundary counts that completed round; elimination during either
side's turn labels the interrupted round explicitly. No score, duration or new
saved counter is invented. Optional appended report metadata preserves older
retry receipts; refresh always reads the full current report. History counts
living saved units with an owner-scoped SQL projection. No migration is needed.

The welcome preview renders `Game.CreateRivergate()` data without creating a
saved game or issuing a command. The report is the single completion summary.
Roving board focus keeps one board tab stop, arrows/Home/End navigate geometry,
and the narrow-screen command post keeps the actual End Turn control visible.
Offline/loading messages never advance the tutorial or change pending command
IDs. Unused scaffold libraries/validation partial/default favicon were removed;
Error and Privacy now contain specific, factual product information. Provider
errors remain logged privately and generic in production responses.
