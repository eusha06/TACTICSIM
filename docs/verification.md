# Verification record

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
