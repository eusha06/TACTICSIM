# Operation Rivergate — Release 0 checklist

## Scope and deployment status

- Requested release: **reliable deployment of the existing application only**.
- Date: 2026-11-01 (Asia/Dhaka).
- Starting commit: `797d3a429bf6453454d562867b1075252ddaedf8`.
- Repository/branch: https://github.com/eusha06/TACTICSIM — `main`.
- No UI, artwork, map, or game-rule changes; do not start Release 1.
- **Not deployed. No production URL or deployed commit has been verified.**
- No established host/config was found; GitHub returned no deployments,
  environments, repository secret names, or homepage URL. This does not rule out
  an unidentified host elsewhere.
- Prepared fallback: root `Dockerfile`, `.dockerignore`, `render.yaml`.
  [Account steps and migration/backup procedure](deployment.md).
- Exact access blockers: no intended Render service/workspace/URL or production
  database identified; no Render CLI/API key configured; available Render
  integration is not installed/connected. An installation/connection option was
  offered. No account, purchase, production database, or hosting service was created.
- Browser blocker: Chrome automation reported “The user changed ... Google
  Chrome” and then `js execution timed out; kernel reset` on both refreshed-state
  and 12-second get-app retries. No new visual browser check is claimed.

## Implementation

- [x] Read AGENTS, README, architecture, verification, actual Web/Core/persistence
  code, CI, and Git/hosting state before editing; baseline Release build passed.
- [x] Update AGENTS for the authorized Operation Rivergate direction and Release 0.
- [x] Multi-stage .NET 10 Docker publish, non-root runtime, no SQLite/database/key
  files in the runtime image, bounded build context with local secrets excluded.
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
- [x] Document backup/restore gates, paid Render pre-deploy command, free-service
  operator alternative, image rollback, and retained-key requirements.
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

## Publishing and live acceptance

- [ ] Commit and push verified source to existing `main` without history rewrite,
  using configured user identity and no assistant co-author attribution.
- [ ] Observe GitHub CI, including the Linux production Docker/TLS/replacement check.
- [ ] Deploy exact successful source through the intended Render service.
- [ ] Record actual service/deployment ID, full deployed commit and real URL;
  compare `/health` revision with Render's deployment commit.
- [ ] Inspect UI in a browser on the deployed version.
- [ ] Verify deployed new/move/attack/end turn/refresh/history/resume.
- [ ] Verify two deployed browser sessions cannot access/control each other's matches.
- [ ] Verify an existing cookie resumes its exact match across a host restart/redeploy.
- [ ] If moving an existing host/ring, verify a pre-release owner cookie and old save.

Blocked checks remain unchecked. CI is build/test evidence, not deployment.

## Short manual play/deploy checklist

1. Complete [Render account setup](deployment.md), including database backup,
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
