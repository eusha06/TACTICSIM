# TacticSim

A small fictional turn-based tactical simulation for a C#/.NET portfolio.

**Current status: playable MVP at [tacticsim.onrender.com](https://tacticsim.onrender.com/).** The Razor Pages board connects to the C# engine using plain JavaScript fetch calls. It includes terrain, unit intel, legal-action highlights, human/computer turns, victory/defeat, New Match, action history, automatic save/resume, and recent completed results. PostgreSQL stores matches, units, ordered events, and accepted commands in relational tables. The Match history page retains earlier matches and can resume an unfinished one. A complete match and refresh/New Match were verified through Chrome controls, along with a 375px mobile layout; see the [verification record and manual checks](docs/verification.md).

**Release 3 implementation:** new operations capture F4 and hold it through two
completed human/computer rounds. Losing occupancy resets progress; losing the
squad fails. Clearing all enemies still requires the hold. Existing saves keep
elimination rules. See [rules](docs/rules.md) and the [release checklist](docs/release-checklist.md)
for current deployment acceptance.

**Release 2 verified live (`7296834`):** a focused briefing with explicit Begin/Continue, in-game help
using C# unit/terrain values, and a three-step introduction driven by selection
and successful moves. Skip/Replay change guidance only. Opening the page never
creates a match. First/returning visits, Skip/Replay, Resume, mobile keyboard play,
security checks and existing-save continuity passed. See the release checklist.

**Release 1 verified live (2026-11-01, `b387483`):** original SVG soldier squads, armored vehicles and field guns,
textured terrain and connected water, clear movement/attack/selection markers,
compact controls and collapsible action history. Rules, saves and HTTP endpoints
are unchanged. [Artwork notes](docs/artwork.md) explain the editable local assets.
Desktop/mobile browser play, a complete live HTTP match, security checks and
existing-save continuity across deployment passed. See the [release checklist](docs/release-checklist.md).

**Release 0 verified live on 2026-11-01:** deployed commit
`a66c3e8b8e2bbc556aeb10cf89c870e064aea9c4`, free Render Docker hosting and
free Prisma PostgreSQL. Health, a complete match, ownership/security checks,
refresh/history/resume, and saved-match continuity across a Render restart passed.
Production cookie keys persist in PostgreSQL. Free Render compute can take about
one minute to wake after inactivity. See the [verification record](docs/verification.md),
[deployment guide](docs/deployment.md), and [release checklist](docs/release-checklist.md).

## Prerequisites

- .NET 10 SDK. `global.json` accepts stable .NET 10 feature bands, including the installed 10.0.402 SDK.
- PostgreSQL running locally or available through a configured connection string. The full solution test run includes real PostgreSQL integration tests; Core tests alone do not need a database.
- No Node tooling is needed to build or run the game. Optional tutorial regression tests use `node --test scripts/tutorial.test.cjs` with no npm packages or frontend build.

## Build and run

From the repository root (the directory containing `TacticSim.sln`):

```sh
dotnet restore TacticSim.sln
dotnet build TacticSim.sln --no-restore
createdb tacticsim
dotnet tool restore --tool-manifest dotnet-tools.json
dotnet ef database update --project src/TacticSim.Web -- --environment Development
dotnet test TacticSim.sln --no-build
dotnet run --project src/TacticSim.Web --launch-profile http
```

Open `http://localhost:5178` to play and `/History` for your browser’s match history. Skip `createdb` if that database already exists. Apply migrations before serving requests. Stop the server with Ctrl+C.

In Development, macOS/Linux defaults to PostgreSQL on `/tmp`, database `tacticsim`, using your OS username (a matching PostgreSQL role must exist). For another setup, configure `ConnectionStrings:DefaultConnection` using user secrets or `ConnectionStrings__DefaultConnection` in the environment. Production requires an explicit connection string and HTTPS. Never commit credentials.

The initial `InitialRelational` migration creates `GameSessions`, `GameMatches`, `MatchUnits`, `MatchEvents`, and `MatchCommands`. Release 0 adds `DataProtectionKeys` for production cookie continuity. There are no `EnsureCreated` calls. An alternative explicit migration command is:

```sh
dotnet run --project src/TacticSim.Web --launch-profile http -- --migrate
```

If you ran the earlier JSON-snapshot MVP, the migration imports its current and older cached matches, unit flags/health, ordered history, and receipt IDs. It leaves the old `Matches`, `Receipts`, and `Results` tables untouched as backups; the application no longer reads or writes them. Old history remains text labeled as legacy events because its original structured fields/command links were never recorded. Legacy start times were not stored, so imported records use completion/import time. Future commands/events are fully structured.

Choose Begin Operation to create a match, or Continue Operation for an unfinished save. During play, select a blue unit, then a round move marker or a dashed orange attack target. How to Play and Replay Tutorial are available without resetting your match. Use End Turn to let the computer act. Refresh resumes the latest match for this browser. The anonymous owner cookie lasts up to 30 days; clearing cookies loses access to that browser’s saved matches. New Match switches to a new battlefield without deleting earlier matches. Use Match history to review any saved operation or Resume an unfinished one. Resume preserves health, turn, and used-action flags; it cannot give a unit another move or attack.

In a restricted execution environment, MSBuild worker processes can stall. These commands successfully restored and built this scaffold without build servers or parallel workers:

```sh
dotnet restore TacticSim.sln -m:1 -nr:false
dotnet build TacticSim.sln --no-restore --disable-build-servers -m:1
dotnet test TacticSim.sln --no-build -m:1 -nr:false
```

The test runner and web server require permission to open local sockets. If the environment blocks sockets, run them in an environment that permits localhost networking.

The EF 10 tool manifest is included. Restore it if necessary:

```sh
dotnet tool restore --tool-manifest dotnet-tools.json
```

With the app running, execute the live integration checks (Python 3 standard library only):

```sh
python3 scripts/verify_web.py
```

This creates separate anonymous test sessions in the local database. It verifies missing/invalid CSRF tokens, owner isolation, forged cookies, duplicate and stale commands, concurrent writes, rejected actions, save/resume, completed results, and a complete human-versus-computer match. It also checks the real Razor history/review pages and Resume forms. It does not replace visual browser QA.

To verify an actual application restart (the script owns its own server on port 5181):

```sh
python3 scripts/verify_restart.py
```

Build Release first: this script starts the Release DLL in Development. It creates a match with damaged health and used actions, terminates the server, starts a new process, and compares restored state/history. It also retries an old attack command to check that its persisted receipt prevents duplicate damage. Cookies stay in memory, and only the script's own processes are stopped.

For the production image (requires Docker and OpenSSL):

```sh
docker build -t tacticsim:release0 .
python3 scripts/verify_deployment.py tacticsim:release0
```

This uses an isolated PostgreSQL container, a temporary trusted TLS proxy, and
container replacement to check production cookie/session continuity. It never
connects to the application's existing database. See [deployment.md](docs/deployment.md)
for the backup/migration procedure and hosted verification steps.

The PostgreSQL xUnit tests use temporary schemas inside the local `tacticsim` database, then remove those schemas. To use another test database, set `TACTICSIM_TEST_CONNECTION` to its connection string; the role needs schema creation permission. They never use EF's in-memory provider or modify the app's public tables. For only the 70 Core tests:

```sh
dotnet test tests/TacticSim.Tests --no-restore
```

## Continuous integration

GitHub Actions builds Release with warnings as errors, runs 102 .NET tests (83 engine,
19 PostgreSQL), plus eight dependency-free tutorial transition checks, verifies/applies migrations, publishes the web app, builds its
production Docker image, and runs TLS/proxy/container replacement checks. The
workflow runs on pushes to `main` and pull requests. CI database credentials are
for disposable services only; local and deployment secrets stay outside Git.

## Engine API

Read [the rules](docs/rules.md) for the unit stats, terrain costs, damage formula, fixed map, and computer decision order. `Unit` is abstract; `Infantry`, `Armor`, and `Artillery` supply their own stats and movement costs. `Game` owns mutable state and accepts commands; callers can only read unit state.

```csharp
using TacticSim.Core;

var game = Game.CreateRivergate(); // CreateDefault() retains legacy elimination.
var legalMoves = game.GetLegalMoves(Side.Human, unitId: 1); // Cell -> shortest cost
var result = game.Move(Side.Human, unitId: 1, new Position(0, 1));
if (!result.IsSuccess)
    Console.WriteLine(result.Error);

// Attack returns an error code or the actual damage dealt.
var legalTargets = game.GetLegalTargets(Side.Human, unitId: 1);
game.EndTurn(Side.Human);
new ComputerOpponent().PlayTurn(game); // Uses the same move/attack commands
Console.WriteLine(game.Winner);
```

Invalid commands leave state unchanged. Legal-move and target queries also leave state unchanged. The engine checks acting side and unit ownership. Web derives ownership from a protected HttpOnly cookie and always submits human commands as `Side.Human`. Razor Pages validates antiforgery tokens; PostgreSQL version checks and unique command receipts prevent stale/duplicate application.

## Layout

```text
TacticSim.sln
src/
  TacticSim.Core/       # Domain and game rules; no framework packages
  TacticSim.Web/        # Razor Pages, HTTP boundary, EF/PostgreSQL
tests/
  TacticSim.Tests/      # Engine xUnit tests; references Core
  TacticSim.Persistence.Tests/ # Real PostgreSQL xUnit integration tests
docs/
  architecture.md      # State flow and security boundaries
  rules.md             # Map, units, movement, combat, computer behavior
  verification.md      # Automated evidence and manual browser checklist
scripts/
  verify_web.py        # Live HTTP integration checks
  verify_restart.py    # Real process restart and retry verification
```

Web and the engine tests reference Core. Persistence tests reference Web. Core references none of these projects. EF packages stay in Web. Package versions are pinned to the versions resolved by the starter; package/tool upgrades should be intentional.

See [AGENTS.md](AGENTS.md) for constraints and the phase checklist, and [the architecture notes](docs/architecture.md) for the implementation plan.
