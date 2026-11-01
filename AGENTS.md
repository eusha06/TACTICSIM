# TacticSim contributor instructions

## Scope and stack

- Build a small fictional tactical simulation for a beginner who knows C++ and data structures. Keep the MVP achievable in about five hours.
- Use .NET 10/C#, ASP.NET Core Razor Pages, plain JavaScript/CSS, EF Core 10 with PostgreSQL, and xUnit.
- No separate frontend framework, Node build pipeline, multiplayer, login, fog of war, external AI APIs, or random combat.
- The user has authorized the engine and playable Razor Pages UI. PostgreSQL persistence and HTTP security are included as necessary foundations for exposing match commands. Keep further work within this small MVP.

## Project boundaries

- `src/TacticSim.Core`: domain models and every game rule. No EF, ASP.NET, database, browser, or HTTP dependency.
- `src/TacticSim.Web`: Razor Pages, server endpoints, anonymous session ownership, and EF/PostgreSQL persistence. Reference Core; do not duplicate its rules.
- `tests/TacticSim.Tests`: xUnit engine tests referencing Core. Add meaningful rule and boundary cases when the engine is implemented.
- `tests/TacticSim.Persistence.Tests`: real PostgreSQL integration tests referencing Web; each run creates and drops only uniquely named test schemas.
- Prefer small types, explicit names, ordinary collections, and straightforward control flow. Avoid generic repositories, CQRS frameworks, and unnecessary abstractions.

## Required MVP behavior

- One fixed 8x8 map with plain, forest, hill, and impassable water.
- Infantry, Armor, and Artillery; three units per side.
- Human versus a simple deterministic computer opponent.
- Select, move, attack, and end turn; terrain-aware movement, attack range, health, and victory detection.
- Save/resume matches, action history, and completed-match results. Call the log **action history**, never a replay feature.
- All rules and legal moves/damage must be computed and validated on the server. Browser selection and highlights are presentation only.
- Bind each match to a separate anonymous browser session. Derive ownership from the server-recognized session, never a client-supplied owner ID; check ownership on reads and writes.
- Protect every state-changing request against CSRF using ASP.NET Core antiforgery validation. Never mutate state on GET.
- Reject stale commands and prevent duplicate commands from applying twice. Persist command IDs and expected state versions with an atomic database update; in-memory checks alone are insufficient.
- Keep secrets out of source control. Do not claim persistence or HTTP security is implemented before its code and checks exist.
- Persist match state, units, accepted commands, and ordered events in relational tables. JSON command-response caches are for retry responses only, never the source used to restore games.
- Use EF migrations only. Never reintroduce `EnsureCreated` alongside migrations. Preserve the legacy snapshot tables as backups; do not drop/reseed existing matches.

## Short implementation checklist

- [x] Phase 1: inspect the starter; document scope, project boundaries, security design, and local commands.
- [x] Phase 1: verify the starter build and the final solution build/test discovery (zero test cases in this phase).
- [x] Phase 2: implement Core rules and deterministic computer turns with meaningful xUnit tests; exact rules are in `docs/rules.md`.
- [x] Phase 3: add PostgreSQL persistence, anonymous ownership, antiforgery, and atomic version/idempotency checks; verify those boundaries using `scripts/verify_web.py`.
- [x] Phase 4: implement the Razor board, plain JS/CSS controls, save/resume, action history, and results; complete a match through live HTTP endpoints.
- [x] Basic Chrome visual/interaction QA: complete match on turn 12, errors, refresh/New Match, and 375px mobile emulation; follow `docs/verification.md` for repeatable and extended checks.
- [x] Relational persistence: generated/applied initial migration, preserved old snapshots as relational rows, added session-owned history/review/resume, and verified an actual process restart with `scripts/verify_restart.py`.

Run commands from the directory containing `TacticSim.sln`. Read `README.md` and `docs/architecture.md` before extending the scaffold. Complete only the phase the user authorizes.
