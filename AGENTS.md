# TACTICSIM: OPERATION RIVERGATE — contributor instructions

## Authorized direction and release gate

- Evolve the existing game into an approachable tactical mission game with an illustrated miniature battlefield and clear instructions, while retaining reliable .NET/PostgreSQL foundations.
- Implement only the bounded release explicitly requested by the user. This direction is not authorization to implement a mission system, new rules, new artwork, or a frontend redesign immediately.
- Read `README.md`, `docs/architecture.md`, `docs/verification.md`, and `docs/release-checklist.md`, then inspect the relevant implementation before editing.
- Preserve working features and existing saved matches. Do not start the next release automatically.
- Prefer small types, explicit names, ordinary collections, and straightforward control flow understandable to a beginner who knows C++ and data structures. Avoid unnecessary abstractions, generic repositories, and CQRS frameworks.

## Stack and project boundaries

- Use .NET 10/C#, ASP.NET Core Razor Pages, plain JavaScript/CSS, EF Core 10 with PostgreSQL, and xUnit. No frontend rewrite, separate frontend framework, JavaScript game engine, or Node build pipeline.
- `src/TacticSim.Core`: domain models and all gameplay rules; no EF, ASP.NET, HTTP, database, or browser dependencies.
- `src/TacticSim.Web`: Razor Pages, server handlers, anonymous ownership, and relational EF/PostgreSQL persistence; reference Core without duplicating its rules.
- `tests/TacticSim.Tests`: meaningful engine and boundary tests referencing Core.
- `tests/TacticSim.Persistence.Tests`: real PostgreSQL integration tests referencing Web. Use a dedicated test database and uniquely named test schemas, never production data.
- No multiplayer, login, fog of war, external AI APIs, or random combat unless a later release explicitly authorizes a scope change.

## Gameplay, compatibility, and security requirements

- Keep every gameplay rule authoritative in C#. The browser sends intent and renders server-provided state, legal moves, and attack targets; it never decides legality or damage.
- Preserve the current playable baseline until a release explicitly changes it: fixed 8x8 map, plain/forest/hill/impassable water, Infantry/Armor/Artillery, three units per side, deterministic computer turns, select/move/attack/end turn, health, and victory detection.
- Preserve save/resume, ordered action history, completed results, and session-owned match history. Action history is not a replay feature; label it accurately.
- Preserve existing save semantics, unit action flags, IDs, map keys, and compatible rules. The current `crossing-v1` key resolves to the existing fixed map; do not silently repoint old saves to a different map or mission. Any authorized schema/rule evolution must include a compatibility plan and tests.
- Bind all match reads, reviews, history, resumes, and mutations to the server-recognized anonymous browser owner. Do not trust client-supplied owner/side IDs or treat a guessed match ID as authorization.
- Preserve Razor Pages antiforgery validation for every state-changing request. GET handlers must not mutate gameplay or active-match selection.
- Preserve command idempotency, payload fingerprint checks, stale-command rejection, and match/session concurrency tokens. Identical retries return their saved outcome without applying an action again, including under concurrent requests.
- Save each accepted command, match/unit/turn/action state, and its ordered events atomically in PostgreSQL. Keep state in relational tables; JSON response caches are only for HTTP retries, not the canonical saved game.
- Use EF migrations only; never combine migrations with `EnsureCreated`. Preserve legacy snapshot tables as backups. Never delete production data, drop/reseed production matches, or point destructive test setup at production.
- Preserve cookie identity and ASP.NET Data Protection compatibility across releases. Production must retain its key ring as well as PostgreSQL data; do not replace keys, change cookie purpose, or reset identity as a routine deployment step.
- Keep credentials, databases, dumps, local key rings, and machine-specific configuration out of Git. Never print secret values in checks or release reports.

## Artwork and experience

- Use original, consistent SVG artwork for precise game assets. Keep assets editable as SVG/code with a coherent perspective, palette, proportions, and stroke treatment. Do not substitute generated bitmap assets, external stock artwork, or a new rendering framework for these assets.
- Make the battlefield read as an illustrated miniature scene while retaining legible terrain, unit labels, health, selection, and actionable server-provided highlights at desktop/mobile sizes.
- Give clear instructions for the gameplay actually available in the requested release. Artwork and labels must not imply mechanics that are not implemented.
- Do not add decorative controls, fake buttons, placeholder mission interactions, or unsupported gameplay claims. Keep implementation details out of player flows unless needed for a meaningful player decision.

## Required process for each requested release

1. Record the release prompt, bounded scope, starting commit, intended branch, hosting target, and acceptance checks in `docs/release-checklist.md`. Inspect current code and hosting configuration rather than assuming previous summaries are current.
2. Implement only that release, preserving saved matches and the requirements above. Continue independent work when an external prerequisite is missing.
3. Run relevant automated checks and fix failures. Preserve engine, persistence, ownership, antiforgery, idempotency, concurrency, migration, and restart coverage as applicable. Record the actual commands, configuration/artifact, and results.
4. Inspect the UI in a browser when access is available, including relevant interactions and responsive states. If access fails, record the exact tool failure and provide precise manual checks; do not claim browser verification.
5. Commit with a clear message and push to the existing intended branch (`main` at the current baseline unless the user changes it). Preserve Git history: no force pushes, rebases, or amendments of existing commits. Use the user's configured author/committer identity and do not add assistant co-author/contributor attribution.
6. Deploy through the established hosting configuration. If the provider, service, credentials, production database/key storage, or target URL is not identified, record the exact blocker and required manual steps. Do not invent a provider, URL, or successful deployment.
7. Verify the deployed URL and the actual deployed commit/artifact, not just a successful CI job or localhost. Check the release's playable flow, persistence/session behavior, and affected UI on the deployed version.
8. Fix observed defects within the release, rerun relevant checks, commit/push, redeploy, and reverify. Do not report completion while an observed release defect remains unresolved; distinguish completed local work from blocked deployment/verification.
9. Update `docs/release-checklist.md` and finish with the commit SHA, deployed URL (or explicit blocked/not deployed status), what changed, checks actually performed, remaining issues/blockers, and a short manual play checklist. Stop before the next release.

A test, deployment, or browser check is only marked passed after observing its result. Historical results in `docs/verification.md` are evidence for that earlier revision, not a substitute for checking a new release. The Development restart script now targets Release binaries. `scripts/verify_deployment.py` separately checks a Production container through a local TLS edge and replaces that container; neither is proof of a deployed Render service.

## Release 2 authorization

- Release 2 is authorized: focused briefing, Begin/Continue, reusable help and an
  action-driven first interaction tutorial. Preserve rules; no capture objectives.
- Page loads/read handlers must never start matches. Begin and any older-match
  Continue must use the existing protected/versioned/idempotent command flow.
- Tutorial selection is presentation state; movement advances only on a successful
  server response for that unit/match/destination. Skip/replay never resets games,
  spends actions, changes pending commands or bypasses keyboard access.
- Derive numeric help from Core and contextual rejections on the server. Preserve
  saves, cookie keys, schema and security; reuse free Render/Prisma. Stop after Release 2.

## Release 1 authorization (completed)

- Release 1 was completed and verified at `https://tacticsim.onrender.com` on
  2026-11-01, deployed source `b3874838971b2320968c44a9cc16427e11f061f3`.
  Its bounded authorization was: illustrated miniature battlefield with original local
  SVG terrain and recognizable Infantry, Armor and Artillery, accessible responsive
  controls and clear action/health feedback. No mission objectives or rule changes.
- Preserve existing endpoints, save data, map/coordinates, victory, ownership,
  antiforgery, command receipts and stale rejection. No migration is needed.
- Reuse the established free Render/Prisma setup; inspect desktop and narrow
  layouts, verify the deployed revision and existing-save continuity. Stop after Release 1.

## Release 0 deployment direction (historical)

As inspected on 2026-11-01: repository `eusha06/TACTICSIM`, branch `main`, baseline commit `797d3a429bf6453454d562867b1075252ddaedf8`.

- Release 0 is authorized: reliable deployment only, with no UI redesign, artwork, or rule changes. Stop after this release.
- No established host was found initially. The authorized Render fallback is now live at `https://tacticsim.onrender.com`, using free Render Docker compute and free Prisma PostgreSQL. Follow `docs/deployment.md` for the established setup.
- CI builds/tests/migrates/publishes and verifies the production Docker image/TLS proxy/container replacement. A green workflow is not a deployment.
- Production must use `ConnectionStrings__DefaultConnection` and PostgreSQL `DataProtectionKeys` storage; never put production databases/keys in the container filesystem. Apply the additive key-table migration explicitly before serving.
- Keep the stable `/app/` container content root and existing owner cookie/purpose. For an existing host's ring, import keys without deleting originals and preserve its exact discriminator using `DataProtection__ApplicationName` before switching traffic.
- Only enable `ReverseProxy__Enabled` behind the managed trusted ingress; it accepts one forwarded scheme, never forwarded host/client identity. Keep `/health` read-only and free of credentials/state details.
- Take/verify a private backup before migrations, preserve legacy backup tables, and never roll back by dropping the key table. Redeploying an older application image must retain compatible key storage/discriminator.
- GitHub inspection returned no deployment records, repository environments, repository secret names, or homepage URL. This does not rule out hosting configured elsewhere; none has been identified for this repository.
- An earlier inspected Render workspace had no TacticSim service/database; the subsequently authorized other account now hosts the dedicated service. The earlier paid draft was never submitted and has been superseded by the user's free-only instruction. Do not modify unrelated services or treat a draft form as a deployment. The Render plugin remains unconnected; browser account access is distinct from plugin access.
- The user subsequently required a **free deployment**. This overrides the earlier paid proposal: do not create paid resources, upgrade existing resources, or add a payment method. `render.yaml` now selects free web compute with an explicit migrate-then-start command, because free compute has no pre-deploy hook. Preserve the backup/review gates and fail startup if migration fails. Prefer an existing suitable free PostgreSQL database; Render's free PostgreSQL expires after 30 days and must be described as a temporary demo, never durable long-term storage. Do not delete or repurpose the user's unrelated free application.
- The user authorized Prisma Postgres and inspection/use of the other Render account, then completed Prisma sign-in personally. Keep EF Core/Npgsql and the .NET architecture; Prisma is a database host, not an authorization for an ORM/Node rewrite. Use pooled `DefaultConnection` for runtime and optional direct `MigrationConnection` only for `--migrate`; preserve the default-only fallback for existing installs. Free Prisma project/database `tacticsim` and free Render service `tacticsim` now exist in Singapore. Keep credentials only in provider secrets; never log them.
- The initial hosted deploy built successfully but exited 127 before application startup with a quoted Docker Command override. The image now uses a versioned `Deployment/start-web.sh` through exec-form CMD. Clear the host Docker Command override and use the image default; do not reintroduce fragile host shell strings. The script explicitly migrates and serves only on success. The corrected hosted commit `a66c3e8b8e2bbc556aeb10cf89c870e064aea9c4` was verified healthy on 2026-11-01, including a complete match, security checks, visible browser play, and exact saved-match continuity across an actual Render restart. Documentation-only updates do not require another runtime deployment.
- Keep actual results, exact deployment/browser blockers, and unverified checks in `docs/release-checklist.md`. Do not start the following release.
