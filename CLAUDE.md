# CLAUDE.md — TacticSim

You are working as a senior game developer on TacticSim. We are rebuilding it into
**TacticSim v2: "Rivergate Command"**, a deterministic WEGO (simultaneous-orders)
tactical simulation with a C# authoritative engine and a Phaser/TypeScript canvas client.

## Read first, every session
1. `docs/v2/GAME_DESIGN_AND_ROADMAP.md`: the spec and phased roadmap. **Source of truth.**
2. `docs/v2/TACTICSIM_Analysis_Report.md`: why we're changing things.
3. The "Carried-over safety rules" section below (it replaces the old `AGENTS.md`,
   which was removed for v2; its scope bans on fog of war, a JS game engine and a
   Node build are lifted by the v2 spec).
4. `docs/release-checklist.md`: record progress here. `docs/v2/decisions.md`: spec calls.

## How to work
- Work on branch `v2-simulation`. Do the roadmap phases **in order**. Finish a phase
  (tests green, acceptance criteria met), commit, give a short summary, then continue
  to the next phase unless I say stop.
- Before coding a phase, write a short plan (files to add/change, tests to write).
- Rules and balance numbers live in `content/*.json`. Never hard-code them in C# or TS.
- `TacticSim.Sim` is pure C# (no ASP.NET/EF). The server is the authority. The client
  mirrors rules **only for previews** and must pass the C# golden test vectors.
- Determinism: sort by unit id, with no unseeded randomness and no wall-clock time in the engine.
- Fog of war is enforced **on the server**. Never send hidden enemy units or orders.
- Keep `TacticSim.Core` (v1) working so old saves still load. Migrations are additive only.
- Keep code beginner-readable: small types, clear names, no CQRS or generic-repository frameworks.
- If a spec detail is ambiguous, pick the simplest option that keeps the rules readable,
  note it in `docs/v2/decisions.md`, and continue.

## Commands
```sh
# .NET
dotnet build TacticSim.sln -c Release --warnaserror
dotnet test TacticSim.sln -c Release
dotnet run --project tools/TacticSim.Balance -- --games 500 --red Veteran --blue Veteran
dotnet run --project src/TacticSim.Web --launch-profile http   # http://localhost:5178

# Client (from client/)
npm ci && npm run typecheck && npm test && npm run build      # outputs to wwwroot/game/
npm run dev                                                    # Vite dev server proxied to :5178
```

## Never
- Delete or reset production data, use `EnsureCreated`, or commit secrets/connection strings.
- Force-push, rebase, or rewrite existing history on `main`.
- Deploy before Phase 7, or claim a check passed without seeing it pass.
- Use copyrighted art, real insignia, or real unit names.

## Carried-over safety rules (from the v1 AGENTS.md)
- Every gameplay rule is authoritative in C#. The browser sends intent and renders
  server state; client rule code is preview-only.
- Bind every match read and mutation to the server-recognized anonymous browser owner
  cookie. Never trust client-supplied owner/side ids or treat a guessed match id as access.
- Antiforgery on every state-changing request. GET handlers never mutate gameplay.
- Command idempotency, payload fingerprints and version (concurrency) checks: an
  identical retry returns the saved outcome and never applies the action twice.
- Save each accepted turn atomically in PostgreSQL. EF migrations only (additive), never
  `EnsureCreated`. Keep legacy backup tables. Never point destructive tests at production;
  tests use uniquely named schemas.
- Keep the Data Protection key ring and owner-cookie purpose stable across releases.
- Keep credentials, dumps, key rings and machine config out of Git; never print secrets.
- Commit with the user's configured git identity only. Do **not** add assistant
  co-author/contributor trailers. No force pushes, rebases or amended published commits.
- Deploy only through the established free Render/Prisma setup (`docs/deployment.md`)
  after a verified backup. Never claim a deploy or live check without observing it.
- Original SVG/code art only; labels never imply mechanics that aren't implemented.
