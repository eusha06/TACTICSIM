# v2 deploy plan: Rivergate Command on the existing free Render + Prisma setup

Status: **planned, not started.** Each gate below needs the owner's go-ahead. Nothing here changes
the host, plan, region, environment variables, cookie keys or database provider.

## What is live, and what this changes

- Hosted revision last observed: `2fcf097` (Release 4). `main` is at `b12ba68` (Release 5, UI only,
  never deployed). Deploying v2 from `main` therefore also ships Release 5's UI changes. Neither
  Release 5 nor v2 alters an existing table.
- **Schema:** one additive migration, `20261123075707_V2Simulation`. Reviewed SQL: three
  `CREATE TABLE` (`matches_v2`, `turn_orders_v2`, `turn_results_v2`), two `CREATE INDEX` and the
  migration-history row. No `ALTER`, `DROP`, `UPDATE`, `DELETE` or `TRUNCATE`.
- **Image:** a new Node stage builds the client before `dotnet publish`. The runtime image, the
  start script (migrate, then serve), environment variables, `/app` content root,
  Data Protection keys and owner-cookie purpose are unchanged. No new secrets.
- **Routes:** adds `/play` and `/api/v2/*`. Classic v1 routes behave as before.

## Rehearsed locally (2026-11-23)

On a throwaway local PostgreSQL 17 database, with both builds run from the same content-root path
(the way production always runs from `/app`):

| Step | Result |
|---|---|
| `main` build (`b12ba68`) migrates an empty DB | 3 migrations, same as production |
| `main` app creates a v1 save (new match + move) | saved, owner cookie captured |
| `pg_dump --format=custom` → `pg_restore --list` → restore into a separate DB | 7 table-data entries; row counts identical (1 match / 6 units / 2 events / 2 commands / 3 migrations) |
| v2 build `--migrate` | `V2Simulation` applied in under 1 s; running it again is a no-op |
| v2 app with the old cookie | the v1 save loads byte-for-byte identically; a v2 Rivergate turn plays; `/api/v2/matches` lists both |
| v2 server memory after play | about 200 MB RSS (free plan limit 512 MB) |
| **Rollback:** `main` build `--migrate` on the upgraded DB, then serve | no-op, exit 0; v1 save identical; v2 rows kept but dormant (`/play` is 404) |

The CI production-container check (`scripts/verify_deployment.py`, 512 MB limit) now also loads
`/play` and its bundle, plays a v2 turn, replaces the container, and confirms the v2 match and the
retried turn's saved result survive. It has not run yet: it runs in CI on the PR.

## Steps

### 1. Pre-flight (no production access)
1. Push `v2-simulation` and open a PR into `main`.
2. CI must pass on the PR: client typecheck, Vitest and build; .NET build and tests; golden-vector
   drift check; balance smoke run; EF pending-model check; migrations; the Docker build; the
   production-container check including the new v2 section.
3. Merge with a normal merge commit (no rebase or force-push on `main`). Record the merge SHA.

### 2. Backup gate (needs the Prisma **direct** connection; run on the owner's machine)
Use a private `~/.pgpass` entry or `PG*` variables for the direct Prisma endpoint. Never put the
password in a command line, chat, Git or a build argument. Check the server first with
`psql -c 'show server_version'`: `pg_dump` must be the same major version or newer (this machine has 17.11).

```sh
umask 077
mkdir -p ~/tacticsim-backups
pg_dump --format=custom --file="$HOME/tacticsim-backups/before-v2-$(date +%Y%m%d-%H%M).dump"
pg_restore --list "$HOME/tacticsim-backups/"before-v2-*.dump | grep -c "TABLE DATA"
```

Then do a dress rehearsal on a **local copy of the real data**, never on production:

```sh
createdb -h /tmp tacticsim_prod_copy
pg_restore -h /tmp -d tacticsim_prod_copy --no-owner --no-privileges "$HOME/tacticsim-backups/"before-v2-*.dump
# Count rows in GameMatches, MatchUnits, MatchEvents, MatchCommands, GameSessions,
# DataProtectionKeys, __EFMigrationsHistory and the legacy backup tables, on both sides.
ConnectionStrings__DefaultConnection="Host=/tmp;Database=tacticsim_prod_copy;Username=$USER" \
  dotnet src/TacticSim.Web/bin/Release/net10.0/TacticSim.Web.dll --migrate --environment Development
# Re-count: every v1 count must be unchanged; the three v2 tables exist and are empty.
```

Record the counts (never the credentials) in `docs/release-checklist.md`. Keep the dump private
and keep it until v2 has run cleanly for a while.

### 3. Deploy (Render dashboard, service `tacticsim`)
1. Before deploying: open one existing v1 save in a private browser profile and keep that profile
   (its owner cookie) for the continuity check afterwards. Release 5 notes mention a captured
   pre-upgrade match; if it can't be found, make a fresh one on the live v1 game now.
2. **Manual Deploy → Deploy a specific commit →** the merge SHA. Leave Docker Command and
   environment variables untouched. Auto-deploy stays off.
3. Watch the deploy log for `Applying migration '20261123075707_V2Simulation'` and
   `TacticSim migrations applied.` A failed migration stops startup. Read the error; don't drop tables.

### 4. Live verification (only mark items passed when actually observed)
- [ ] `/health` returns `healthy` and its `revision` equals the merge SHA.
- [ ] The captured v1 save still opens with its old cookie: same board, health and history.
- [ ] `/play` loads; first request after idle shows "Waking the server…" and recovers.
- [ ] Boot Camp played to the after-action report.
- [ ] Rivergate against Veteran: at least 2 turns, refresh mid-planning, resume from the menu.
- [ ] A second browser can't open the first browser's v2 match (404).
- [ ] No console errors on `/play`; the phone layout is usable.
- [ ] v1 table row counts are unchanged from the backup counts (plus any new live play).
- [ ] Record the SHA, Render deploy ID and results in `docs/release-checklist.md`.

## Rollback

- **App-only rollback is safe (rehearsed):** redeploy the previous commit (`2fcf097` or
  `b12ba68`) in Render. Its startup `--migrate` is a no-op, v1 saves keep working, v2 rows stay in
  their tables unused, and `/play` disappears until v2 is redeployed.
- Never run the Down migration or drop the v2 tables as a rollback. Restoring the backup is a
  separate, deliberate recovery decision: it loses every write made after the backup.

## Risks and limits

- **Cold start:** the free instance sleeps; the client shows a "waking" message after 2 s.
- **Memory:** about 200 MB measured locally; CI checks a v2 turn under a 512 MB container limit.
- **Storage:** a 12-turn match stores about 35 KB (compressed JSONB tick logs). 10,000 finished
  matches would be about 350 MB. Check the Prisma free quota before promoting the game widely.
- **Build time:** the Node stage adds an `npm ci` + Vite build (about a minute) to each Render build.
- **Connections:** a v2 turn is a handful of queries plus one transaction; the pooled limit of 5 is enough.
- **Optional, separate change:** narrow `AllowedHosts` from `*.onrender.com` to
  `tacticsim.onrender.com` (noted in `docs/deployment.md`). Not bundled with this deploy.

## Who does what

| Step | Who |
|---|---|
| Push branch, open PR, read CI | Owner pushes; Claude can open the PR and read CI with `gh` once the branch is pushed |
| Merge to `main` | Owner approves |
| Backup + dress rehearsal | Owner (holds the Prisma credential); Claude can run the local half once a dump exists, without seeing secrets |
| Render manual deploy | Owner in the dashboard |
| Live verification | Claude can drive the checks in the built-in browser; the owner confirms the v1 continuity cookie |
