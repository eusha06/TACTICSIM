# Verification record

## Hosted startup repair — 2026-11-01

The user completed Prisma sign-in. Dedicated Free Prisma `tacticsim` project and
database were created in Singapore; private pooled/direct settings were stored
in a new Free Render service. Render assigned `https://tacticsim.onrender.com`.
Initial deploy `dep-dbjis40ae00c73ec61pg` of `83406f0` built successfully but
exited 127 before application startup. The quoted host command was inspected;
the URL is not yet verified healthy.

Moved the startup sequence into versioned `Deployment/start-web.sh`, invoked by
the image's exec-form CMD; the host override must be cleared. Local Docker
verification of that default command passed under 512 MB, including empty-DB
bootstrap, direct migration override, TLS/full match (turn 16/six attacks),
security/isolation/history/resume, container replacement and outage/recovery.
Corrected live deployment and acceptance checks remain pending.

## Prisma Postgres preparation — 2026-11-01

The user authorized Prisma Postgres plus another Render account, retaining a $0
budget. The other account was observed on its first-service creation screen.
Prisma login was reached; the user chose to complete account sign-in personally.
No cloud database/credential or TacticSim web service has been created or verified.

Added only optional direct migration configuration: `--migrate` can use
`ConnectionStrings__MigrationConnection`, falling back to DefaultConnection.
Gameplay, Data Protection and normal requests still use DefaultConnection.
This supports transaction-pooled runtime connections without routing EF's
session-level migration lock through a transaction pooler. No ORM rewrite.

Release build passed with zero warnings/errors and all 83 tests passed. The
production Docker check also passed under 512 MB: fresh bootstrap, full match
(turn 14/seven human attacks), TLS/CSRF/ownership/races/history/resume, container
replacement and outage/recovery. A separate migration process succeeded with a
valid direct override while its default string was intentionally unreachable.
These are isolated local PostgreSQL results, not evidence of Prisma connectivity
or a deployed Render application. Live checks await completed account sign-in.

## Free-hosting correction — 2026-11-01

The user requires free hosting; no paid service or upgrade is authorized. The
existing unrelated Render app was observed on the Free plan. TacticSim's manifest
now selects free web compute, removes the paid pre-deploy hook, and explicitly
runs migrations before starting the server. No UI or game-rule code changed.

Release build and all 83 tests passed. The Docker check passed using the exact
free start command under a 512 MB memory limit, including bootstrap of an empty
PostgreSQL database, migration reapplication, TLS/cookies/security, a complete
match (turn 14, seven human attacks), history/resume, container replacement with
persisted keys/state, and database outage/recovery. All Docker data was disposable.

Live deployment remains pending the PostgreSQL choice. Render's free database
expires after 30 days and is subsequently deleted; no temporary database has
been created or represented as permanent storage. The user was asked for an
existing free PostgreSQL resource or acceptance of that demo limit. Existing
matches/backups remain intact. Paid proposals below are historical, superseded
by the free-only instruction. See [the current setup guide](deployment.md).

## Release 0 — 2026-11-01

Release build/publish passed with zero warnings/errors. All **83 tests** passed
(70 Core, 13 PostgreSQL); EF reported no model drift. The new tests cover database
key persistence between independent providers, compatibility/import of an old
filesystem cookie key ring, and healthy/missing-schema readiness without writes.

The actual Production Docker image passed `scripts/verify_deployment.py` through
a temporary trusted local TLS proxy: PORT binding/non-root user, HTTPS redirect,
Secure/HttpOnly cookies/HSTS, a complete match (turn 14, seven human attacks),
CSRF, two-owner isolation, duplicate/stale/concurrent commands, refresh/history/
resume, and completed results. Replacing the web container preserved the exact
owner cookie, damaged match/action flags/history and an old attack receipt.
PostgreSQL outage produced 503 readiness; recovery restored the exact saved state.

After a private local database backup, the additive key-table migration applied
successfully. Before/after counts and content checksums matched for all five
canonical and three legacy backup tables. A separate disposable restore also
matched those checksums. No production database was identified or modified.

Observed/fixed: the valueless `--migrate` argument could swallow a subsequent
`--environment`; startup now strips operator switches before host parsing.
Production checks also corrected test assumptions about HttpOnly casing and
localhost HSTS exclusion, then passed on rerun.

The final implementation `4c2e3779c388387bcc3ec56971404628f89e10cf` also passed
[GitHub Linux CI](https://github.com/eusha06/TACTICSIM/actions/runs/41300576818),
including all 83 tests, migration/model checks, publish, Docker build, source
revision check, published JavaScript/CSS, the exact Render migration command,
TLS/full match/security checks and container replacement. Repeated local games
ended on turns 14 and 16 depending on the deliberate concurrent move race.

**No deployed URL/check is claimed for Release 0.** Initial Chrome automation
failed with focus-change errors/timeouts; access recovered on follow-up.
At pushed revision `b03468de7571b016eb60831a94e4f67696ea73f4`, Release build,
83 tests, EF consistency and the production Docker/TLS/replacement check all
passed again. A real private Chrome session also verified New Match, all unit
types moving, attack (Armor 16→11 HP), End Turn, refresh, History and Resume.
The same browser retained health, positions, turn, history and used Artillery
actions across an actual local server restart. This visual check used the
Release DLL in Development on port 5183; Production key persistence was checked
separately by replacing the Docker container through a local TLS edge.

The original database/legacy table checksums still match the retained backup
after excluding only that new test owner. Existing matches and backups remain
intact. The fresh full HTTP match ended on turn 16 with six human attacks.

Render's signed-in workspace is now accessible in Chrome. The inspected project
has no TacticSim service/database. Draft creation forms show $7/month web compute
and $6.30/month PostgreSQL 17 with 1 GB storage; recurring-charge approval is
pending. No resources were created. The Render plugin remains unconnected.
Follow
[deployment.md](deployment.md) for precise account steps and
[release-checklist.md](release-checklist.md) for pending live checks. Historical
browser results below describe the earlier MVP only.

## Automated checks

- Solution build: zero warnings and errors.
- xUnit: 70 passing engine/snapshot/history tests plus 9 real PostgreSQL tests (79 total). Persistence tests cover fresh migrations, legacy conversion, state restoration, ownership, resume limits, concurrent commands, stale versions, rollback after an injected PostgreSQL event-insert failure, and a deterministic duplicate-command race between the receipt and version reads.
- JavaScript syntax: `node --check src/TacticSim.Web/wwwroot/js/site.js` passed. Node is only an optional development check; the app has no Node pipeline or runtime dependency.
- Live PostgreSQL/HTTP checks: `python3 scripts/verify_web.py` passes against the running app. It uses real HTTP requests and independent cookie jars, not an in-memory database.
- Full matches completed on turns 14 and 16, with seven and six human attacks respectively, and the computer winning. The concurrency check can choose a different first human move, so the two runs need not have identical outcomes. Both sides' moves/attacks were recorded, and completed state rejected further commands. New Match retained the completed result.
- Security checks exercise missing/invalid antiforgery tokens, anonymous-owner isolation, tampered cookies, invalid opponent commands, replaying a command ID, reusing it with another payload, stale versions, concurrent identical requests, and different concurrent requests at the same version.
- A page reload preserved the same saved state. Core JSON round-trip tests also verify health, positions, side, turn, action flags, winner, and state isolation.

## Relational persistence and restart verification

- Generated and applied `20261101061527_InitialRelational`; EF migration history records the application.
- Converted the existing database's 8 owner sessions and 12 cached matches into 72 unit rows, 230 ordered event rows, and 111 command rows. Original legacy tables remained unchanged as backups.
- A dedicated PostgreSQL integration test also verifies the legacy import and that a second migration application is a no-op. Every integration run uses temporary schemas, not application tables.
- Updated live HTTP checks verify `/History`, owned match review, rejection of foreign review/resume, CSRF on the Resume form, duplicate Resume, preserved used-action flags, and consecutive event sequence numbers.
- `python3 scripts/verify_restart.py` passed using a real stop/start on port 5181. The same browser cookie restored exact state including wounded health, positions, current side/turn, both versions, spent move/attack actions, and ordered history. Retrying the pre-restart attack returned its receipt and did not inflict damage twice.
- Checked code has no `EnsureCreated` calls. Connection secrets, database dumps/data, local key directories, and dotenv files are ignored by Git.

## Chrome browser verification

Browser automation became available on the follow-up verification. A separate private Chrome window was used so the normal browser's match was left intact.

- Inspected the desktop board, terrain colors, unit labels/health, selection details, legal movement markers, and attack target outlines.
- Moved Infantry, Armor, and Artillery through the board; verified readable server errors for a second move, an over-budget forest move, and moving after an attack.
- Verified ranged and melee attacks update health and append accurate action history.
- Completed a private-window match through visible browser controls on turn 12. The computer won; the completion banner appeared and gameplay stopped.
- Refreshed the completed match and verified the result/history persisted. Canceled New Match, then confirmed it; the board reset and the completed result remained visible.
- Inspected Chrome's iPhone SE emulation at 375×667 and standard browser zoom. All eight board columns fit; unit names/health and terrain colors were visible. Selected/moved Infantry, then inspected turn controls, unit details, squad status, and action history below the board.
- Tightened grid minimum sizing and explicit board rows for small layouts. Corrected the manual opening below: D2 is forest and is not an initial legal Artillery destination.

This covers Chrome desktop and mobile emulation, not a physical phone or every browser. The checklist below remains useful for keyboard, additional browsers, multiple tabs, and interrupted-network checks; server-side race/retry/isolation behavior is covered separately by the HTTP checks.

## Manual desktop/mobile checks

Run the app as described in the README, then open `http://localhost:5178`.

1. Confirm an 8x8 board with A–H / 1–8 labels, all four terrain colors, a terrain legend, three blue units and three orange units. Check that unit names and HP are readable.
2. Select blue Infantry at A1 (or use Your squad). Confirm the selected outline, Unit intel, and green legal-move markers with costs. Move it to A3. Its position, movement status, and action history should update.
3. With that Infantry still selected, click A4. The server should report that it already moved, with no position/turn/health change. Water clicks should also produce a useful server error when attempted by a unit with movement available.
4. Select Armor at B1 and move to B4. Select Artillery at C1 and move to E1. (D2 is forest and exceeds Artillery's initial movement budget.) Press End Turn. Opponent actions should appear in the history, followed by a new human turn and reset action availability.
5. When an enemy is in range, select a blue unit and confirm orange attack-target outlines. Attack; HP and history should update. Try moving the same unit after its attack and confirm rejection. Artillery should not highlight adjacent enemies.
6. Continue selecting legal moves and targets and ending turns until one squad is eliminated. Confirm a victory/defeat banner, disabled End Turn, and a completed result under Quick briefing. Actions must stop once the match completes.
7. Refresh mid-match and after completion; confirm that the latest state and history resume. Start New Match, cancel once, then confirm it; the board should reset while completed results remain.
8. Open a private/incognito browser window. Confirm its independent fresh match. Returning to the original window must retain its own match. In two tabs of the same session, act in one then attempt an action from the stale tab; it should refresh with a conflict message.
9. Resize to 375px width and tablet/desktop widths. Check all eight columns fit, no horizontal overflow, readable unit labels, and accessible controls/history. Use Tab/Enter, arrow keys on board cells, and Escape to check keyboard interaction.
10. Interrupt the server while making an action. Confirm an error and Retry; after restarting, Retry must apply that command at most once. A missing/expired session should prompt a page reload.

The visible log is **action history**, not a replay feature.
