# Operation Rivergate — release checklist

## Release 4 — restrained action presentation (current)

- Starting commit `6343c271d5b638944e5847b4981a319dea94a58b`, branch `main`.
- Target: existing free Render `srv-dbjis38ae00c73ec5vl0`,
  https://tacticsim.onrender.com/, existing Prisma PostgreSQL and key ring.
- [x] Inspect engine/routes, event/receipt persistence, board rendering and host.
- [x] Expose actual routes and authoritative ordered frames, preserve old receipts.
- [x] Add short movement/attack/health/destruction feedback and ordered opponent turn.
- [x] Lock input, safe retry/refresh, reduced motion, Skip/Escape and repeated-activation guard.
- [x] Automated route/frame, persistence/security and presentation checks; local browser desktop/mobile.
- [x] Push verified source and observe green CI.
- [ ] Deploy exact source and verify old saves and live interactions.
- [ ] Record evidence/manual checklist; stop after Release 4.

Implementation source: `bf5f11d3a4df276ae4f428dc00ac33348460e8d7`
(after `137ff619928c1c10c819ada1df8aebab98bf0c88`).
[Final source CI passed](https://github.com/eusha06/TACTICSIM/actions/runs/41420556070):
108 .NET tests (87 Core, 21 PostgreSQL), 16 tutorial/presentation tests,
migration/model/publish, production Docker/TLS, actual mission win/defeat,
security/isolation/concurrent commands and container replacement/outage checks.
Release build has zero warnings/errors. No new migration or EF model changes.

Local Chrome evidence: animation inspector recorded movement and attack effects;
Armor F2→F3 with deliberately slowed playback was skipped with Escape, retaining
the accepted destination. Attack reported six damage and changed target HP
16→10. Emulated 375×667 layout inspected. Reduced-motion emulation displayed the
completed opponent turn immediately with correct deaths/health. DevTools Offline
left Artillery at D2, E1 empty and input locked; restoring connectivity and Retry
moved to E1 once, increasing history from 24 to 25 entries. Final source also
ignores double-clicked End Turn and held activation keys; that last browser input
case remains unobserved after desktop automation failed. Automated backend
concurrency and presentation cancellation checks passed.

Deployment is pending, not complete. Public /health still reported Release 3
`dfe0495ec1c467733ddd6518fba5c76c60050d9a` during pre-upgrade capture. Browser
access initially failed with `cgWindowNotFound`; the user reopened Chrome/Render.
The page then became readable, but input failed/missed with
`windowNotFoundAtPosition` and invalid element IDs, including after rebinding.
No working Render connector/CLI/deploy hook is configured. A manual deployment
was requested; no deployment success or Release 4 live check is claimed.

A pre-R4 Rivergate match, its used move, original receipt and owner cookie were
recorded privately for exact postdeployment comparison. Historical temporary
pre-Release-1 cookie fixtures no longer exist in this session; legacy/imported
snapshot and old-receipt behavior are covered by PostgreSQL tests. No production
schema, cookie purpose/key storage, credentials or existing matches were reset.
No sound or subsequent release was added.

Manual deployment and acceptance:
1. In [the existing service](https://dashboard.render.com/web/srv-dbjis38ae00c73ec5vl0),
   choose Manual Deploy → Deploy a specific commit. Paste
   `bf5f11d3a4df276ae4f428dc00ac33348460e8d7`, select its row, Deploy commit.
   Keep the current free compute, environment variables and Docker command.
2. Wait for Deploy succeeded | Live. Open https://tacticsim.onrender.com/health;
   require healthy and that exact revision. No migration should be newly applied.
3. Continue an existing game. Move along land, attack a target, End Turn; watch
   ordered opponent actions, actual HP and destruction. Try Skip animation/Esc,
   refresh during a sequence and History/Resume. No extra action should apply.
4. Double-click End Turn and hold Enter: only one intended turn should submit.
   Test reduced motion and a narrow viewport; controls should unlock promptly.
5. Complete capture or defeat and inspect the concise result. Run the HTTP
   verification/mission scripts against the live origin and compare the privately
   recorded pre-upgrade save/receipt before marking this release complete.

Stop after Release 4; deployment/live acceptance remains open.

## Release 3 — Rivergate capture and hold (complete)

- Starting commit: `7873573`, branch `main`; date 2026-11-02 (Asia/Dhaka).
- Target: existing free Render `srv-dbjis38ae00c73ec5vl0`,
  https://tacticsim.onrender.com/, existing Prisma PostgreSQL/key ring.
- [x] Inspect engine/restoration, relational model, old SQL import and receipt cache.
- [x] Implement explicit Rivergate mission; additive defaults keep all old matches elimination.
- [x] Prove F4 reachable/unoccupied, capture/reset/defeat/restoration/duplicate safety.
- [x] Play a winning mission against meaningful deterministic opposition.
- [x] Update objective artwork, instructions/tutorial, history/results/resume display.
- [x] Back up/rehearse migration preserving original rows, keys and legacy tables.
- [x] Release build, engine/PostgreSQL/HTTP/tutorial/deployment checks and browser layouts.
- [x] Commit/push, green CI, deploy and verify exact live source and old/new saves.
- [x] Record evidence and stop after Release 3.

- Deployed source: `dfe0495ec1c467733ddd6518fba5c76c60050d9a`.
  [Corrected CI passed](https://github.com/eusha06/TACTICSIM/actions/runs/41344313480):
  83 engine + 19 PostgreSQL tests, eight tutorial tests, migrations, publish,
  production Docker/TLS, complete mission win/defeat and container replacement.
  Initial CI exposed a split-query event-counter race; fixed with consistent
  graph loads and repeated concurrent-round coverage before deployment.
- Render `dep-dbjpkccs728c738ffjbg` showed **Deploy succeeded | Live**.
  `/health` returned 200/healthy with the exact source SHA. Published SVG,
  fingerprinted CSS/site JS/tutorial JS matched source byte-for-byte.
- Private local and hosted backups were restored and upgraded in separate local
  databases before production. After deployment, every original hosted row's
  old-column hash remained present across all six tables, including the key ring.
  All 21 old matches retained Elimination/null/zero defaults; no reset/reseed.
- Retained pre-Release-1 cookie restored exact turn-3 legacy game; old attack
  receipt replayed without applying damage again; protected Resume preserved it.
- Full public HTTPS suite passed CSRF, forged cookie/two-owner isolation,
  stale/idempotent/concurrent commands, completed locking, history/review/Resume,
  and New Match preserving results. Actual capture victory turn 17 and defeat
  turn 14 passed, including 1/2-round refresh/New Match/Resume and duplicate hold.
- Native Chrome played the live winning route: enemy contested F4, killed two
  friendlies; clearing all enemies left 0/2. Artillery occupied F4 for 1/2.
  Requested Render Restart service; logs confirmed new instance `qrlxr` started
  after `5zzbh`, with no pending migrations. Same private cookie then restored
  Turn 15, 1/2 and Artillery 8 HP. History/Resume preserved them; End Turn yielded
  2/2 and Mission success on turn 17. No extra command or round credited on reload.
- Desktop and emulated 375×667 layouts inspected, including empty/occupied
  outpost, progress, success and help. Live mobile Replay Tutorial + select
  Infantry + Down/Enter move reached the capture/End Turn explanation only after
  the accepted A1→A2 move, with keyboard focus retained.

No access blocker or observed functional defect remains. Free compute cold
starts remain; mobile was emulated rather than tested on physical devices. The
opponent is deterministic and greedy; this verifies a meaningful contest and
winning route, not exhaustive strategy/balance analysis. Stop after Release 3.

Manual play check:
1. Begin Operation; find the F4 outpost and 0/2 indicator. Follow or replay the tutorial.
2. Advance by legal land routes and attack threats. Keep a living blue unit on F4.
3. End Turn: the count advances only after the opponent finishes. Moving off or
   losing the holder resets it; losing the squad fails. Clearing enemies still
   requires holding and ending rounds.
4. At 1/2 refresh, open History and Resume; verify progress/health/actions remain.
5. Complete another held round for success. Review the result; an older save
   still says Legacy elimination and wins by defeating its opposing squad.

## Release 2 — briefing and guided interaction (complete)

- Starting commit: `b09c985ce663696776c7568defa5c53f8319a255`, branch `main`.
- Scope: briefing/Begin/Continue, Core-derived help, successful-action tutorial,
  skip/replay and contextual server errors. No rule/objective/schema changes.
- Established target: free Render `srv-dbjis38ae00c73ec5vl0`,
  https://tacticsim.onrender.com/, existing Prisma PostgreSQL/key ring.
- [x] Inspect instructions, docs, rendering, command flow and hosting.
- [x] Implement read-only welcome, protected Begin/Continue and help/tutorial.
- [x] First/returning visit, rejected actions, skip/replay and refresh tested locally;
  retry receipts and unrelated responses covered by tutorial/HTTP tests. Live Resume passed.
- [x] Release build zero warnings/errors; 70 engine + 14 PostgreSQL tests; eight
  tutorial tests; complete local HTTP suite and no EF model drift. Desktop and
  344×656 mobile briefing/help inspected; moved buttons above secondary notes
  after observing excessive mobile scrolling.
- [x] Pushed `72968349fc871327f2fb9870e074f3c249193b0d` under the configured
  user identity; [CI passed](https://github.com/eusha06/TACTICSIM/actions/runs/41335056072),
  including eight tutorial tests and production publish/TLS/container replacement.
- [x] Render `dep-dbjo1o7b9u1s73ejq310` showed **Deploy succeeded | Live**;
  `/health` returned 200/healthy with that exact SHA. All published JS/CSS/SVG
  matched source byte for byte. Desktop and 375×667 mobile UI inspected.
- [x] Live fresh browser: help/history created no match; explicit Begin started one.
  Opponent selection and water rejection did not advance lessons; accepted
  Infantry A1→A3 did. Skip survived refresh. New Match retained the first save;
  Resume opened it directly, with its moved flag. Replay explained Already moved.
- [x] Live mobile Continue, Space selection, Down/Enter move Armor B1→B2,
  focus restoration, Got it, End Turn/computer response and help all passed.
- [x] Local mobile offline request left tutorial step two unchanged. Restoring
  connectivity and Retry moved Armor once, appended one event and advanced it.
- [x] Full deployed HTTP suite passed: no match on read, owned Continue fallback,
  complete match (turn 10, six attacks), CSRF/session isolation, stale/idempotent/
  concurrent commands, action limits, history/results/Resume and save retention.
- [x] Pre-Release-1 cookie still restored EXACT match
  `17fbcda5-242e-4980-89d0-b1a6672f20b5`, turn 3, 11-HP Armor, units/flags,
  versions and ordered events. Continue discovery did not mutate it; protected
  Resume preserved it. No migration, key replacement or database reset.
- [x] Record actual evidence, push documentation and stop after Release 2.

No functional defect remains from these checks. Free compute can cold-start;
mobile was emulated, not tested on physical devices. No access blockers remain.

Manual check: open a private session → How to Play → History (no match yet) →
Begin → select blue unit → highlighted move → read attack/End Turn step → Skip,
refresh and Replay → Briefing/Continue → New Match then History/Resume. On phone,
check help scrolling and use Tab/arrows/Enter/Space/Escape with a keyboard.

## Release 1 — illustrated battlefield (complete)

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
  Deployed responsive inspection also passed after the label-spacing correction.
- [x] Commit/push `b3874838971b2320968c44a9cc16427e11f061f3`; observed
  [successful CI](https://github.com/eusha06/TACTICSIM/actions/runs/41331025556),
  including published SVG availability and production TLS/container replacement.
- [x] Render deployment `dep-dbjnbo49v7es73ak5p00` showed **Deploy succeeded**;
  `/health` returned HTTP 200/healthy and that exact runtime revision.
- [x] Published SVG and fingerprinted CSS/JavaScript returned 200 and matched
  the committed files byte for byte. Deployed desktop and 375×667 mobile
  screenshots inspected; corrected labels no longer overlap adjacent cells.
- [x] Pre-deployment owner cookie restored EXACT match
  `17fbcda5-242e-4980-89d0-b1a6672f20b5`, including turn 3, all units/positions,
  11-HP enemy Armor, spent actions, versions and ordered history. Resume preserved them.
- [x] Full deployed HTTP gameplay/security suite passed: complete match (turn 14,
  seven attacks), owner isolation, CSRF, stale/idempotent/concurrent commands,
  history/review/resume, completion locking and New Match preserving results.
- [x] Deployed mobile browser: select/move all three types, arrow focus/Escape,
  End Turn/computer response, move Artillery G1 and attack G5 (16→11), unit details,
  refresh, history/help disclosures, New Match cancel/confirm, and Resume of the
  earlier wounded match all passed. No gameplay/server/persistence files changed.
- [x] Record actual checks, commit/push release documentation, stop after Release 1.

Remaining limitations: free-host cold starts; mobile was emulated in Chrome,
not tested on physical devices. No functional defect remains from these checks.
No mission objectives or following-release features were added.

Manual play check: select each miniature → inspect round move markers → move →
End Turn → select Artillery and a dashed attack target → inspect health/history →
refresh → New Match → Match history → Resume the earlier match. Use Tab/arrows,
Enter/Space and Escape to confirm keyboard access. At narrow widths, scroll to
End Turn and selected-unit details below the board.

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

Release 0 stopped at its original gate. The separately authorized Release 1 is
recorded above; Release 2 was subsequently authorized and is now complete.
Historical Release 2 gate; Release 3 is now separately authorized above. Stop after Release 3.
