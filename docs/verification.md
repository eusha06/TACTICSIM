# Verification record

## Release 3 local acceptance (2026-11-02)

Release build with warnings as errors passed: zero warnings/errors. All 102 .NET
tests passed (83 Core, 19 PostgreSQL), and eight tutorial transition tests passed.
Python scripts compile, site.js syntax and whitespace checks pass. The HTTP
regression suite passed CSRF, separate-owner reads/mutations, forged cookies,
stale versions, identical/concurrent retries, rejected-action rollback,
new/history/review/resume and completion locking against the Release DLL.

`verify_mission.py` completed an actual victory (turn 17) and defeat (turn 14).
The opponent occupied F4 and killed Infantry and Armor; Artillery survived with
8 HP. Eliminating the enemy left the game active at 0/2; F4 occupancy followed
by two End Turns produced 1/2 then 2/2. Refresh, duplicate/stale End Turn,
New Match/Resume and review at 1/2 preserved exact progress and unit state.
Core tests cover temporary lost occupancy, holder death, all unit types,
restoration, old snapshots, defeat and legacy elimination. PostgreSQL tests
cover concurrent duplicate evaluation, save/resume, transactional event failure,
and old imported matches/receipt responses without mission metadata.

Both the local database and the actual hosted Prisma public schema were backed
up privately, restored into separate disposable local databases and upgraded.
All original-column table checksums matched after rehearsal; all 21 hosted
matches defaulted to Elimination. Hosted backup included 1 DataProtectionKeys,
21 matches, 12 sessions, 149 commands, 271 events and 126 units. Archives remain
outside Git in private temporary directories; legacy tables/backups were retained.
No production migration or deployment is implied by these rehearsal results.

Local native Chrome showed the F4 marker and 0/2 guidance, accepted moves for
all three unit types and a computer advance that occupied F4 and wounded
Infantry. A complete native-board victory also passed (turn 19 after one extra empty
round), including refresh and History/Resume at 1/2, and 375×667 success layout.
CI exposed an event-loading race: split queries could combine an old counter
with new event rows. Match graph loads now use one statement; resuming the
current match reuses that loaded graph. A repeated concurrent-round test covers
this boundary; the corrected 102-test local run passed. Further live evidence
is recorded in the release checklist.

## Live Release 2 acceptance

https://tacticsim.onrender.com/ serves
`72968349fc871327f2fb9870e074f3c249193b0d`. Render deployment
`dep-dbjo1o7b9u1s73ejq310` showed **Deploy succeeded | Live**. Health returned
HTTP 200/healthy with that exact revision. [CI passed](https://github.com/eusha06/TACTICSIM/actions/runs/41335056072),
including 84 .NET tests, eight tutorial tests, migrations/publish, production
Docker, TLS/proxy checks and container replacement. Published SVG, CSS, site JS
and tutorial JS were compared with source byte for byte and matched.

A test owner cookie retained since before Release 1 restored exact match
`17fbcda5-242e-4980-89d0-b1a6672f20b5` after Release 2: same turn 3, wounded
11-HP Armor, positions, action flags, match/session versions and ordered history.
Continue discovery returned that owned match without changing it. Protected
Resume preserved its units, flags and turn. No schema/key/rule reset was performed.

The full live HTTPS suite passed with certificate validation: first-visit reads
leave state empty; Continue prefers the active unfinished match or finds an owned
older unfinished one after completion; full match (computer victory, turn 10,
six human attacks); ownership, CSRF, forged-cookie rejection, duplicate/stale/
concurrent commands, action limits, history/results and Resume all passed.

Observed live Chrome checks:

- Returning session offered Continue and Begin; desktop welcome inspected.
- A fresh private session showed Begin only. Opening How to Play and then History
  still showed No saved matches yet. Only explicit Begin opened the board/tutorial.
- Opponent selection did not advance step one; friendly selection did. Attempting
  water returned the server error while step two stayed active. Successful Infantry
  A1→A3 advanced to the attack/End Turn explanation and restored cell focus.
- Skip stayed dismissed after refresh. New Match retained the earlier operation;
  History/Resume reopened `553c9eb4-d38c-4202-a1ed-96a879c4ea0d`, with Infantry
  A3 and its spent move. Replay did not reset it and explained Already moved.
- 375×667 iPhone SE emulation inspected welcome and help. Continue preserved the
  operation. Space toggled Armor selection, Down navigated B2, Enter submitted a
  legal B1→B2 move, and only then did the tutorial advance. Got it, End Turn,
  computer response and help passed. No physical-device test is claimed.

Local retry check additionally used DevTools Offline: a failed Armor B1→B2
request kept the lesson at step two and kept the same pending intent. Restoring
No throttling and clicking Retry moved once, appended one history event and
advanced to step three. Connectivity settings were restored.

No functional defect remains from these checks; free-host cold starts remain.
Only release-record documentation changed after the deployed implementation.
Stop after Release 2.

## Release 2 local acceptance

- Release build passed with warnings as errors; 70 engine and 14 PostgreSQL
  tests passed. New integration test checks server-generated too-close/too-far
  range explanations and unchanged state/receipts after rejection.
- Eight plain-JavaScript tutorial tests passed using Node's built-in test runner,
  without npm packages or a frontend build: friendly selection, matching accepted
  moves, retries, unrelated/old state, per-match refresh, Skip and Replay.
- EF reports no model changes. Core, schema, keys and game rules are unchanged.
- Full local HTTP suite passed, including first-visit read-only briefing/help/history,
  owned Continue fallback to an older unfinished save after active completion,
  complete match (turn 10, six attacks), CSRF/isolation/concurrency/idempotency,
  stale commands, history/results and Resume.
- Desktop Chrome first visit showed no board/match until explicit Begin. Opening
  help and then History still showed no saved matches. Begin started the normal
  operation. Opponent selection did not advance step one; water rejection did
  not advance step two; an accepted Infantry A1→A3 move reached step three.
- Got it, Replay on already-moved Infantry, a rejected second move, Skip,
  refresh and Continue all retained the same state. Skip stayed dismissed;
  Replay used actual remaining actions. End Turn did not pretend to be a move;
  a subsequent legal A3→A4 move completed that step.
- 344×656 Chrome mobile emulation inspected welcome and scrollable help. Action
  buttons were moved above secondary notes after inspecting excessive scrolling.
  No physical mobile device was tested. Live deployment evidence is recorded above.

## Live Release 1 acceptance — 2026-11-01

https://tacticsim.onrender.com/ is serving
`b3874838971b2320968c44a9cc16427e11f061f3`. Render deployment
`dep-dbjnbo49v7es73ak5p00` showed **Deploy succeeded** on the existing free
service. `/health` returned HTTP 200, `healthy`, and that exact revision.
[CI for this source passed](https://github.com/eusha06/TACTICSIM/actions/runs/41331025556):
Release build, all 83 tests, migration/publish checks and production Docker/TLS/
container replacement checks. The production asset check now includes the
versioned SVG. Live SVG and fingerprinted CSS/JavaScript bodies were compared
with the committed files and were byte-identical.

A private pre-deployment test owner retained its cookie across deployment.
Match `17fbcda5-242e-4980-89d0-b1a6672f20b5` restored **exactly**, including
versions, turn 3, all units/positions/health, spent actions and ordered history.
Resume retained its 11-HP enemy Armor and used Artillery actions. No migration,
rule/endpoint change, key-ring replacement or data reset was introduced.

The complete HTTP suite passed against the deployed HTTPS origin with verified
TLS: computer victory on turn 14 after seven human attacks; action limits,
New Match/results, refresh/history/review/resume, independent owners, foreign
access rejection, antiforgery, forged cookies, duplicate/stale commands and
concurrent writes. These checks created only their own anonymous test matches.

Visible Chrome checks on the deployed version:

- Desktop layout and iPhone SE 375×667 emulation inspected. Original blue/orange
  soldier squads, tracked vehicles and field guns render; forest clusters,
  raised hills, restrained grass and connected water match the fixed terrain.
  Corrected narrow labels fit their cells. No crossing spans water.
- Infantry A1→A3, Armor B1→B4, Artillery C1→E1, End Turn and computer response.
  Artillery E1→G1 then G5 attack: Armor 16→11 HP, health bar changes and action lock.
- Move completion restored focus; Right navigated to B3 and Escape deselected.
  Native buttons retain Enter/Space activation. Reduced-motion CSS disables
  transitions/animations; no movement animation was added.
- Squad selection and compact details, refresh, action history and help disclosures,
  New Match cancellation and confirmation, preserved earlier match in History,
  then Resume returning turn 3 and the 11-HP Armor. Browser test match:
  `b7bb0755-09d3-40dd-b95e-84800ecf2f41`.

No functional defect remains from these checks. Mobile checks used emulation,
not physical hardware; free-host cold starts remain. Only release-record docs
were updated after the deployed implementation. Stop after Release 1.

## Release 1 local checks — 2026-11-01

- Release build with warnings as errors: zero warnings/errors.
- xUnit: 70 engine + 13 real PostgreSQL integration tests passed.
- EF pending-model check: no migration/model changes.
- `python3 scripts/verify_web.py http://127.0.0.1:5184` passed the complete
  human/computer match (computer won on turn 16, six human attacks), all action,
  history/result/resume, antiforgery, owner-isolation, idempotency, stale and
  concurrent-command checks.
- `node --check` accepted plain JavaScript; SVG parsed as XML; diff whitespace clean.
- Chrome desktop screenshot inspected original artwork and layout. iPhone SE
  emulation at 375×667 inspected the entire 8×8 board, blue/orange silhouettes,
  grass/forest/hill terrain and connected water without crossings. A small label
  overlap was observed and compact label spacing was corrected before deployment.
- Mobile browser selected Infantry and moved A1→A3; focus returned to the cell,
  Right moved keyboard focus to B3 and Escape deselected. Armor B1→B4 and
  Artillery C1→E1, End Turn/computer response, Artillery E1→G1 and attack G5
  passed. Opponent Armor went 16→11 HP; spent attack and locked movement appeared.
  Refresh retained state. Action history disclosure showed the five-damage event.
- Native browser access intermittently timed out or changed active windows;
  successful observations above were made after raising the dedicated test window.

Live deployment and final responsive acceptance are recorded above separately
from these local results.

## Live Release 0 acceptance — 2026-11-01

Verified https://tacticsim.onrender.com/ on deployed commit
`a66c3e8b8e2bbc556aeb10cf89c870e064aea9c4`. Render service
`srv-dbjis38ae00c73ec5vl0`, deployment `dep-dbjji7m7bikc73flln40` showed
**Live**. Logs showed successful migrations, Production and HTTP binding on
`0.0.0.0:10000`. `/health` returned HTTP 200, `healthy`, and the exact commit
before and after restart. [CI for this source passed](https://github.com/eusha06/TACTICSIM/actions/runs/41306607485)
(Release build, 70 engine tests, 13 PostgreSQL tests and production deployment checks).

The existing `scripts/verify_web.py` was run against the HTTPS public URL using
Python's certificate-verified TLS context with `certifi` roots. It passed:

- Full human/computer match: computer victory on turn 14 after seven human attacks;
  completed-state action locking and New Match retaining completed results.
- Moves, attacks, end turn, refresh, ordered history, review and resume.
- Independent browser-owner sessions, foreign review/resume rejection, forged
  cookie rejection and opponent-unit control rejection.
- Missing/invalid antiforgery tokens, idempotent retries, stale commands,
  concurrent identical/different commands, and unchanged state after illegal actions.

Visible private Chrome checks confirmed board rendering, Infantry A1→A3,
Armor B1→B4, Artillery C1→E1, computer response after End Turn, then Artillery
E1→G1 attacking Armor G5 for 5 damage (16→11 HP). Spent action flags, the
post-attack movement lock, refresh, History and Resume were inspected.
New Match passed the deployed HTTP checks; its visible confirmation dialog was
inspected, but its final browser confirmation was not submitted during this check.

Render Events confirmed actual service restarts at 19:13 and 19:14 Asia/Dhaka.
The same private browser cookie subsequently restored match
`a3e76977-e82c-4ea2-971f-b2f5b6ed57f5`: turn 3, Infantry A3, Armor B4,
Artillery G1 with spent actions, opponent Armor G5 at 11/16 HP, and the original
12 ordered history events. Resume succeeded after restart. This verifies
PostgreSQL match storage and Data Protection owner-cookie continuity on the host.
No functional defect was observed in these checks. Free-host cold starts remain
an operational limitation; no paid resources or credentials were committed.

The sections below record earlier preparation and local checks; their pending
hosting statements describe those earlier stages and are superseded by this acceptance.

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
