# Operation Rivergate — release checklist

## Release 1 — illustrated battlefield (current)

- Scope: original SVG Infantry/Armor/Artillery and terrain, connected water,
  dominant responsive battlefield, accessible selection/action/health feedback.
  No mission objectives, gameplay, endpoint, database or victory changes.
- Starting commit: `95d4ac7df9d2eb499cb6ed21411be8b6f5ec00a2`; branch `main`.
- Established target: free Render service `srv-dbjis38ae00c73ec5vl0`,
  https://tacticsim.onrender.com/, existing free Prisma PostgreSQL and key ring.
- [x] Inspect instructions, architecture, verification, rendering, handlers and hosting.
- [x] Implement original local SVG assets and responsive accessible board.
- [x] Release build, 70 engine/13 PostgreSQL tests, no model drift, full HTTP checks.
- [x] Inspect desktop and 375×667 layouts, keyboard focus and browser move/attack/turn/refresh/history.
  Final deployed responsive inspection remains required after the label-spacing correction.
- [ ] Commit/push, observe CI, deploy exact source, verify live revision and UI.
- [ ] Verify existing saved-match continuity; record results and stop after Release 1.

## Release 0 — completed

### Scope and deployment status

- Requested release: **reliable deployment of the existing application only**.
- Date: 2026-11-01 (Asia/Dhaka).
- Starting commit: `797d3a429bf6453454d562867b1075252ddaedf8`.
- Repository/branch: https://github.com/eusha06/TACTICSIM — `main`.
- No UI, artwork, map, or game-rule changes; do not start Release 1.
- **Verified live URL: https://tacticsim.onrender.com. Release 0 complete.**
  Runtime commit `a66c3e8b8e2bbc556aeb10cf89c870e064aea9c4`; health and
  live acceptance passed on 2026-11-01. No following release started.
- No established host/config was found; GitHub returned no deployments,
  environments, repository secret names, or homepage URL. This does not rule out
  an unidentified host elsewhere.
- Prepared fallback: root `Dockerfile`, `.dockerignore`, `render.yaml`.
  [Account steps and migration/backup procedure](deployment.md).
- Follow-up baseline: `b03468de7571b016eb60831a94e4f67696ea73f4`, already pushed
  with [successful CI](https://github.com/eusha06/TACTICSIM/actions/runs/41300826593).
  Runtime implementation required no further change during revalidation.
- Render browser account access has now been observed. The inspected project
  has one unrelated Node service and no TacticSim service/database. The Render
  CLI is absent and its plugin is not installed/connected; neither prevents
  using the signed-in browser when resource creation is approved.
- The user subsequently specified **free hosting only**. The paid proposal is
  withdrawn; no paid resource, upgrade or payment method is authorized.
- The existing unrelated service was directly observed on Render's **Free**
  compute plan. It remains unchanged. `render.yaml` now also selects `plan: free`.
- Current target: the user authorized Prisma Postgres and the other Render
  account, with free resources only. The dedicated service is now live in that account.
- The user completed Prisma sign-in. Free workspace quotas were observed: 200K
  operations/month, 2 GB storage, $0 invoice. Dedicated project/database `tacticsim`
  was created in Singapore; pooled/direct credentials were privately configured
  in the new free Render service. No secret values were committed or printed.
- Render service `srv-dbjis38ae00c73ec5vl0`, assigned `https://tacticsim.onrender.com`,
  Free Docker/512 MB, Singapore. Initial deploy of `83406f0` failed before C#
  startup; clearing the host override and using the versioned image startup
  script fixed it. Live deployment `dep-dbjji7m7bikc73flln40` uses
  `a66c3e8b8e2bbc556aeb10cf89c870e064aea9c4`. Existing local data/backups remain unchanged.
- Chrome automation initially reported focus-change errors/timeouts; subsequent
  recovery allowed local and subsequently deployed browser checks. See
  [live acceptance evidence](verification.md) for the exact checks and restart state.

## Implementation

- [x] Read AGENTS, README, architecture, verification, actual Web/Core/persistence
  code, CI, and Git/hosting state before editing; baseline Release build passed.
- [x] Update AGENTS for the authorized Operation Rivergate direction and Release 0.
- [x] Multi-stage .NET 10 Docker publish, non-root runtime, no SQLite/database/key
  files in the runtime image, bounded build context with local secrets excluded.
  Exec-form `CMD` invokes the versioned migrate-then-start script; keep the
  provider Docker Command empty. The container check runs that default twice. PostgreSQL readiness uses TCP to avoid
  accepting its temporary initialization-only Unix socket.
- [x] Match actual `ConnectionStrings__DefaultConnection` PostgreSQL configuration.
- [x] Honor `PORT` on `0.0.0.0`; preserve explicit URL overrides. Opt-in managed
  proxy HTTPS scheme handling before cookies/redirects, without forwarded host/IP.
- [x] Read-only `/health`: required PostgreSQL schema, 200/503, no owner/game/key
  creation, no secret/details in response, revision identifier, no-cache.
- [x] Production Data Protection keys stored in PostgreSQL; stable container root
  and unchanged owner cookie/purpose. Development local identity remains intact.
- [x] Additive `20261101105015_PersistDataProtectionKeys` migration; no game-table
  changes or deleted legacy backups. Explicit migration hook, never EnsureCreated.
- [x] One-time additive/idempotent old key-ring import command and discriminator
  compatibility documentation/test; no keys deleted/replaced.
- [x] Document backup/restore gates, the free Render migrate-then-start command,
  image rollback, retained-key requirements, sleep/usage limits and DB expiry.
- [x] Preserve C# authority, session ownership, antiforgery, accepted-command
  atomicity, idempotency, concurrency and stale-command rejection.

## Checks actually performed locally

All results below are local/test evidence, not a Render deployment check.

- [x] `dotnet build TacticSim.sln --configuration Release --no-restore
  --warnaserror --disable-build-servers -m:1`: zero warnings/errors after fixes.
- [x] `dotnet test TacticSim.sln --configuration Release --no-build --no-restore
  --nologo -m:1`: **83 passed** — 70 engine, 13 real PostgreSQL tests.
- [x] `dotnet ef migrations has-pending-model-changes --project
  src/TacticSim.Web --configuration Release --no-build -- --environment
  Development`: no model drift. Idempotent upgrade SQL reviewed: only new key
  table and EF migration-history insert.
- [x] Release `dotnet publish` succeeded; Python scripts compile; `git diff --check` clean.
  Published JavaScript/CSS links returned 200 through the TLS test edge.
- [x] `docker build --build-arg SOURCE_REVISION=release0-local -t
  tacticsim:release0 .` succeeded using .NET 10 SDK/runtime images.
- [x] `python3 -u scripts/verify_deployment.py tacticsim:release0`: Production
  container + disposable PostgreSQL 17 + temporary trusted local TLS edge passed.
  Verified non-root UID, PORT 10000, HTTPS redirect/proxy scheme, Secure/HttpOnly
  owner cookie, and HSTS on a non-local hostname.
- [x] The same HTTPS test completed a match (computer victory, turn 14, seven
  human attacks), including new/move/attack/end turn/refresh/history/resume,
  completed-state locking and preserved older results.
- [x] Separate cookie jars cannot read/review/resume/control another owner's
  matches. Missing/invalid CSRF, forged cookies, opponent control, duplicates,
  stale versions, concurrent same/different commands and action limits verified.
- [x] Replaced the production web container (fresh filesystem) while retaining
  PostgreSQL: exact owner cookie/state, wounded health, used actions, versions,
  history and old attack receipt survived; duplicate attack did not apply twice.
- [x] Stopped disposable PostgreSQL: `/health` returned 503. Restarted it:
  readiness returned 200 and the exact match state remained available.
- [x] Backed up the local existing database privately, applied the new migration,
  compared all row counts/content checksums before/after: unchanged across five
  canonical tables and three legacy backup tables. Counts: 11 sessions,
  17 matches, 102 units, 297 events, 147 commands; legacy 8 Matches,
  111 Receipts, 4 Results. Existing local keys were not changed.
- [x] Rehearsed restoring the backup into a separate disposable database; all
  eight table counts/content checksums matched. Removed only that new test DB.
  Backup remains outside Git at
  `/private/tmp/tacticsim-release0-backup-y0s9bf0o/before-release0.dump` (private
  permissions). A backup from the initial failed attempt was also retained.
- [x] Fixed an observed migration CLI bug: bare `--migrate` consumed a subsequent
  `--environment`; operator flags now removed before ASP.NET argument parsing.
  Also corrected test assumptions for HttpOnly attribute casing and localhost
  HSTS exclusions; all affected checks reran successfully.

### Follow-up verification of the pushed source

- [x] Rebuilt Release with warnings as errors; all 83 tests passed again and EF
  reported no model drift at `b03468d`.
- [x] Rebuilt `tacticsim:release0` with `SOURCE_REVISION=b03468de7571b016eb60831a94e4f67696ea73f4`;
  reran the complete production TLS/PostgreSQL/container-replacement script with
  that expected revision. All checks passed; the match ended on turn 16 with six
  human attacks. It used only disposable Docker resources.
- [x] Real private Chrome window on `http://127.0.0.1:5183`: selected and moved
  Infantry A1→A3, Armor B1→B4, Artillery C1→E1; ended the turn and saw the computer
  response. Moved Artillery E1→G1 and attacked Armor G5: 16→11 HP, with the exact
  five-damage event and spent actions visible.
- [x] Refreshed, opened History, and used Resume. Turn 3, damaged health, positions
  and Artillery's locked move/used attack persisted. Desktop board inspected in
  a screenshot; no UI/rule changes made.
- [x] Stopped that local server, started a new process, and refreshed the same
  private browser. Its cookie still restored the original match/history/action
  limits. This used a Release DLL in **Development**; production database keys
  were separately verified by the Production container-replacement check.
- [x] Confirmed New Match through its dialog, observed a fresh board, then used
  History to resume the earlier unfinished operation with its 11-HP enemy and
  spent Artillery actions intact. Both operations remained listed.
- [x] Excluding only this new browser-test owner, all five original game tables
  and three legacy backup tables still match the counts/content checksums in
  the retained pre-release backup. No earlier matches/backups were changed.
- [x] Inspected actual Render creation forms, selected PostgreSQL 17/1 GB with
  autoscaling off, and prepared web Docker/migration/health settings from the
  public repository. Forms are drafts only; creation awaits spend approval and
  the real database connection must be supplied after provisioning.

## Publishing and live acceptance

### Free-hosting correction

- [x] Changed only hosting configuration/verification/docs: free web compute,
  no paid pre-deploy hook, explicit migrate-then-start Docker command. Server
  rules, UI, ownership, antiforgery and command semantics are unchanged.
- [x] Release build passed with zero warnings/errors; all 83 tests passed.
- [x] Production image + exact free start command passed under a **512 MB**
  container limit. It bootstrapped an empty PostgreSQL schema before readiness,
  safely reapplied migrations, completed a match (turn 14, seven human attacks),
  verified TLS/cookies/CSRF/two-owner isolation/history/resume, replaced the
  container retaining state/keys, and checked database outage/recovery.
- [x] Dedicated free Prisma PostgreSQL target identified and configured; no
  expiring Render PostgreSQL database was created.
- [x] Deploy the free service and verify the real assigned URL/commit, play flow,
  two-browser ownership isolation, and resume after redeployment.

### Prisma connection preparation

- [x] Read Prisma's current official connection/pooling docs and pricing. Prisma
  Postgres supports standard PostgreSQL clients; keep EF Core/Npgsql without a
  Prisma ORM/Node rewrite. Verify the actual account's Free plan before creation.
- [x] Added optional `ConnectionStrings__MigrationConnection` for `--migrate`
  only. Runtime always uses `ConnectionStrings__DefaultConnection`. Empty or
  missing override preserves the original one-string configuration.
- [x] Release build passed with zero warnings/errors; all 83 tests passed.
- [x] Production Docker check passed: 512 MB free start, empty-schema bootstrap,
  full match (turn 14/seven attacks), security/isolation/history/resume, container
  replacement and DB outage/recovery. A separate `--migrate` process succeeded
  using a valid direct override while its runtime connection intentionally
  pointed to an unreachable port, verifying the override without cloud access.
- [x] Documented pooled runtime/direct migration endpoints, bounded client pools,
  TLS, private secret configuration and unchanged fallback behavior.
- [x] User completes Prisma sign-in; identify/create a dedicated Free TacticSim
  database and app credential, preserving unrelated databases/credentials.
- [x] Configure both private Npgsql strings in free Render, deploy verified source,
  and perform the live acceptance checks above. No cloud result is inferred from
  local PostgreSQL tests.

### Hosted startup repair

- [x] Observed successful Render image build and exit 127 on the initial host
  command before app/migration startup. No working deployment claimed.
- [x] Added a versioned `Deployment/start-web.sh`, copied into the image and
  invoked by exec-form CMD. Clear the provider Docker Command override; the
  script explicitly applies migrations, stops on error, then execs the server.
- [x] Rebuilt/retested the default image command under 512 MB: bootstrap,
  migration override, full match (turn 16/six human attacks), all HTTP security,
  history/resume, container replacement and DB outage/recovery passed locally.
- [x] Push, observe CI, deploy the corrected image, and complete live checks.
  [Successful corrected-source CI](https://github.com/eusha06/TACTICSIM/actions/runs/41306607485),
  deployed SHA `a66c3e8b8e2bbc556aeb10cf89c870e064aea9c4`.

- [x] Commit and push verified source to existing `main` without history rewrite,
  using configured user identity and no assistant co-author attribution.
  Implementation: `4c2e3779c388387bcc3ec56971404628f89e10cf` (following initial
  `e0234fce14cbfc921139ba3fdceb11e9f5559eb9`). Author and committer use `eusha06`,
  without co-author attribution. A later documentation-only release-record commit
  does not change that tested implementation; the final report identifies HEAD.
- [x] Observe GitHub CI, including the Linux production Docker/TLS/replacement check:
  [successful implementation run](https://github.com/eusha06/TACTICSIM/actions/runs/41300183058)
  for `e0234fc`; the final CMD/TCP-readiness/published-asset follow-up also passed:
  [successful final implementation run](https://github.com/eusha06/TACTICSIM/actions/runs/41300576818)
  for full implementation SHA `4c2e3779c388387bcc3ec56971404628f89e10cf`.
  Both Linux runs completed successfully. CI is not a deployment.
- [x] Deploy exact successful source through the intended Render service.
- [x] Record actual service/deployment ID, full deployed commit and real URL;
  compare `/health` revision with Render's deployment commit.
- [x] Inspect UI in a browser on the deployed version.
- [x] Verify deployed new/move/attack/end turn/refresh/history/resume.
- [x] Verify two deployed browser sessions cannot access/control each other's matches.
- [x] Verify an existing cookie resumes its exact match across a host restart/redeploy.
- [x] Existing-host key import: not applicable to this new hosted installation.
  Original local matches, legacy tables and migration backups remain intact.

Live acceptance is complete; CI and actual deployed checks are recorded separately.
Browser play verified moves/attacks/turn/refresh/history/resume; New Match and
two independent owner sessions were additionally verified through live HTTP checks.
Render restarts retained the same cookie, match ID, turn, health, actions and history.
Current `AllowedHosts` is `*.onrender.com`; narrow to the assigned hostname on
a future configuration update. Free compute cold starts remain an operational limit.

## Short manual play/deploy checklist

1. Complete [free Render account setup](deployment.md), including database backup,
   secret Npgsql connection, actual AllowedHosts, migration hook and managed ingress.
2. Deploy the exact verified `main` commit. Record the actual URL/ID and confirm
   `/health` reports healthy and its revision equals that commit.
3. In browser A: New Match, select/move, make a legal attack when in range,
   End Turn, refresh, History, New Match, then Resume the earlier unfinished match.
   Check health, positions, action flags, turn and ordered history persist.
4. In independent private browser B: create a match; A's copied History match ID
   must return 404, a Resume POST must be rejected, and A's state must not change.
   Keep antiforgery validation and stale/duplicate rejection enabled.
5. Leave A's cookie intact, record its match/health/actions/history, restart or
   redeploy the same service, then refresh/resume and compare. Check an older
   pre-release save if one exists. Finish a match and review its retained result.

Stop after Release 0. No following-release implementation is authorized here.
