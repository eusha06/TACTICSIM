# Release 0 — free Render deployment

The user requires a **free deployment**. The earlier paid proposal was not
submitted and is superseded. Do not create paid resources, upgrade another app,
or add a payment method. The existing unrelated application was observed using
Render's Free compute plan; TacticSim can use that same plan as a separate Docker
service. No TacticSim deployment URL has yet been assigned or verified.

`render.yaml` selects **free** web compute and an explicitly supplied PostgreSQL
connection. It creates no database. The fixed .NET 10 image, server rules,
PostgreSQL game state, Data Protection keys and security boundaries are unchanged.

## PostgreSQL choice and free limits

Prefer an existing suitable free PostgreSQL database, with private credentials
and sufficient retention for saved matches. Do not borrow credentials or change
the database used by an unrelated application. Keep the original database and
backups intact if changing providers; rehearse a full restore instead of silently
starting an empty replacement for an existing hosted game.

Render's own free PostgreSQL is a **temporary demo option**: it expires after
30 days, then is deleted after a further 14-day grace period unless upgraded.
It provides no managed backups. Database deletion would also remove
`DataProtectionKeys`, making owner cookies unreadable. Use this option only with
explicit acceptance of its time limit, export private backups before expiry,
and migrate before expiry if games must remain accessible. A database-retention
choice was requested; no temporary database has been created yet.

Free Render web services sleep after 15 idle minutes and can take about a minute
to wake up. Filesystem changes do not survive restarts; keys and matches must
remain in PostgreSQL. The workspace shares 750 free instance hours per month
across free services, including the user's existing app. Observe workspace
bandwidth/build quotas; the free plan is not an unlimited usage commitment.
Do not enable paid upgrades or add-ons. These limits come from
[Render's free-instance documentation](https://render.com/docs/free).

## Account steps

1. Use the signed-in Render workspace. Leave the existing unrelated service
   unchanged. Identify the intended PostgreSQL resource, its retention policy,
   backup procedure and TLS details. PostgreSQL **17** matches integration tests;
   select it explicitly if provisioning a new database. For a temporary Render
   demo, select **Free ($0)**, fixed 1 GB storage, with no paid options. Confirm
   the displayed expiry date and record it in the release checklist.
2. Choose **New Web Service → Public Git Repository** and enter
   `https://github.com/eusha06/TACTICSIM.git`. This works without broadening the
   existing Git Provider permissions. Alternatively use root `render.yaml` on
   `main` if the repository is already authorized for Blueprints.
3. Match the manifest's settings:

   | Setting | Value |
   | --- | --- |
   | Source / branch | Public repository above / `main` |
   | Language | Docker (automatically detected) |
   | Compute | **Free — $0**, 512 MB; no disks/add-ons |
   | Region | Same as PostgreSQL; Singapore for the inspected workspace |
   | Root directory | Empty (repository root) |
   | Build context / Dockerfile | `.` / `./Dockerfile` |
   | Health check | `/health` |
   | Pre-deploy command | **Empty**; unavailable on Free compute |
   | Auto-deploy | Off; deploy reviewed commits manually |

   Set **Docker Command** to the following exact command (also in `render.yaml`):

   ```sh
   /bin/sh -c 'dotnet TacticSim.Web.dll --migrate && exec dotnet TacticSim.Web.dll'
   ```

   This explicitly applies reviewed migrations before starting the web process.
   A migration failure prevents startup. A restart reruns the EF migration check,
   which is a no-op when current; EF's database migration lock coordinates it.
   Backups/review remain required **before** each new schema deployment. Normal
   application startup outside this host override does not migrate automatically.
4. Set **`ConnectionStrings__DefaultConnection`** in Render's secret environment
   to an **Npgsql key/value string**, not the provider's `postgresql://` URL:

   ```text
   Host=<database-host>;Port=5432;Database=<database-name>;Username=<database-user>;Password=<secret>;SSL Mode=VerifyFull;GSS Encryption Mode=Disable
   ```

   Use the actual provider's TLS policy/certificate chain. Do not disable
   certificate verification for an external connection. Password/TLS hosting
   disables optional Kerberos GSS encryption; TLS remains enabled and verified.
   Never commit the connection string or pass it as a Docker build argument.
5. Add `ASPNETCORE_ENVIRONMENT=Production`, `PORT=10000`, and
   `ReverseProxy__Enabled=true`. Set `AllowedHosts` to the actual assigned service
   hostname, without scheme/port. If Render assigns it only after creation,
   bootstrap using `*.onrender.com`, then narrow to the real hostname before
   acceptance. Include intended custom domains explicitly; do not guess a URL.
6. Verify backup/review gates and CI for the exact pushed commit before clicking
   **Deploy web service**. Confirm the compute price is $0 and no paid extras
   are selected. The first deployment can begin immediately on creation.
7. Record the actual service ID, assigned URL, commit and database expiry (if
   applicable). Compare `/health`'s `revision` with Render's deployment commit.
   Play the deployed game, verify two-session isolation, then restart/redeploy
   and resume with the same browser cookie. Keep live checks unchecked until
   actually observed.

The pending prerequisite is a usable PostgreSQL target: either an existing free
database or explicit acceptance of Render's temporary demo database. Browser
account access is available; no paid-resource approval is being requested.

## Container and proxy behavior

The multi-stage Dockerfile restores/publishes only Core/Web with the .NET 10 SDK,
then runs the ASP.NET 10 runtime as its non-root `app` user. Its fixed working
directory is `/app`. No PostgreSQL files, dumps, key files, SDK, or test tooling
are copied into the runtime image. `.dockerignore` limits the source context.
The exec-form `CMD` starts `dotnet TacticSim.Web.dll`. Free Render overrides it
with the migrate-then-start shell command above. The Docker check bootstraps an
empty database using that exact command under a 512 MB limit, then reapplies the
migration and replaces the web container to check state/key continuity.

`PORT` binds HTTP on `0.0.0.0` (default image port 8080 if absent); explicit
`--urls`/`ASPNETCORE_URLS` overrides it. Render supplies 10000. TLS terminates at
Render's edge. `ReverseProxy__Enabled=true` accepts one `X-Forwarded-Proto` value
before HSTS, redirection, and cookie creation. It accepts no forwarded host or
client-IP identity. This explicit cloud setting trusts the directly connected
proxy's scheme: use it only with Render's managed public ingress and no exposed
direct container port/untrusted internal callers. On another host, restrict
ingress/trusted proxies to that host's actual network before enabling it. Do not
set the broader `ASPNETCORE_FORWARDEDHEADERS_ENABLED` flag in addition.

Production redirects application HTTP requests to HTTPS port 443. `/health` is
also available over internal HTTP for Render probes and never sets cookies or
creates game state. It reads required session/key tables, returns 200/`healthy`
when reachable, or 503/`unhealthy` on database/schema failure. Details/credentials
are not returned. It does not apply migrations. The response is not cached.
`revision` uses Render's `RENDER_GIT_COMMIT`; elsewhere pass
`--build-arg SOURCE_REVISION=<full-sha>` when building the image. An `unknown`
revision is not proof of a deployed version.

## Persistent anonymous ownership

Production Data Protection keys live in the relational **`DataProtectionKeys`**
table in the same durable PostgreSQL database, using Microsoft's EF key store.
Container replacement needs no persistent local disk. Keep this table and all
historical keys in every backup: removing keys loses the ability to read owner
cookies and antiforgery tokens even if matches remain. Restrict access and use
the managed database's encryption at rest, encrypted backups, and TLS policy.
The XML key material is not separately encrypted by the application; PostgreSQL
and its backups are part of the credential trust boundary. Do not log/export the
XML to public artifacts.

Cookie name `TacticSim.Browser`, purpose `TacticSim.AnonymousOwner.v1`, and the
default application discriminator are unchanged. Development keeps its existing
local key store. New containers retain the same `/app/` content root across
deploys; never change this discriminator, hostname/cookie scope, or database as
a routine deployment action. The cookie still expires after 30 days; clearing
it still loses that browser's access.

If an existing hosted app used filesystem keys, **before switching traffic**:

1. Back up the old ring and database; retain old files. Record the old exact
   `DataProtectionOptions.ApplicationDiscriminator` (normally the normalized
   content root, including its trailing separator). Set
   `DataProtection__ApplicationName` on the new service to that exact value.
   Do not choose a new name for an existing installation.
2. Apply the migration. From a trusted operator machine/container with the same
   database environment, import a private copy of the old XML directory:

   ```sh
   dotnet /path/to/published/TacticSim.Web.dll --import-keys /private/path/to/old-ring
   ```

   This one-time command only adds missing key/revocation records in a
   transaction; it can be rerun, does not print XML, and never replaces or
   removes keys. Do not bake the directory into an image. If old keys were
   encrypted using a certificate/Windows DPAPI, provide a compatible decryptor
   and its private material on the new host before migration; copying encrypted
   XML alone is insufficient. Do not switch traffic until an old owner cookie
   successfully restores its original match. No existing host/ring was found
   to import in this release.
3. Keep the previous database/ring backups, imported keys, discriminator, and
   original hostname. Test an existing cookie across replacement, not just a
   newly created match.

## Safe migrations and recovery

Normal startup never migrates, reseeds, or calls `EnsureCreated`. The explicit
`--migrate` command exits after `Database.MigrateAsync`. Run one deployment at a
time against the intended database; EF's PostgreSQL migration lock coordinates
the migration itself. Do not point test scripts/fixtures at production.

Before each schema deployment:

1. Identify the exact service/database and reviewed source commit; pause writes
   using the host's maintenance window when needed. Confirm there is enough
   backup retention and storage. Record the existing EF migration list.
2. Take a provider snapshot and/or a full custom-format `pg_dump`, including
   `__EFMigrationsHistory`, `DataProtectionKeys` if present, and the old
   `Matches`, `Receipts`, `Results` backup tables. Use a private `.pgpass`/provider
   credential mechanism, not passwords in shell arguments; `PG*` variables here
   are libpq settings, distinct from the application's Npgsql connection string:

   ```sh
   umask 077
   pg_dump --format=custom --file=/private/backup/tacticsim-before-release.dump
   pg_restore --list /private/backup/tacticsim-before-release.dump
   ```

   A listing proves readability, not restore completeness. Rehearse restoration
   into a **separate empty verification database** and verify counts/old saved
   states before proceeding. Never use `pg_restore --clean` on the live database.
3. Review migration SQL using the SDK/EF tool on the checked-out commit:

   ```sh
   dotnet tool restore --tool-manifest dotnet-tools.json
   dotnet ef migrations script --idempotent --project src/TacticSim.Web --configuration Release --output /private/backup/release.sql
   ```

   Release 0 adds only `DataProtectionKeys`. The original migration still imports
   legacy snapshots when necessary and leaves its source tables intact. No game
   rows or old backups are deleted by this release. Never use a down migration
   for rollback: its key-table drop would lose ownership keys.
4. After backup review, deploy the new image with the explicit free
   migrate-then-start command. Failed migrations prevent startup; inspect the
   error rather than dropping tables. Re-running an applied migration is a no-op.
   Do not put database credentials in the image or run migrations during build.
5. Verify health, saved-match counts and old cookies, then allow traffic. This
   additive schema permits rolling the **application image** back while keeping
   the schema/keys. An old binary that uses filesystem keys still needs its old
   ring/discriminator; keep both backups. Any database restore requires a
   separate, deliberate recovery plan and write freeze, never automatic cleanup.

An operator with database access can alternatively apply the published
`--migrate` command before deployment, then use the default server-only Docker
command. Free compute has no provider shell or one-off jobs; do not rely on those
paid features to prepare its schema. No paid plan is required by this repository.

## Reproducible checks

```sh
dotnet build TacticSim.sln -c Release --warnaserror
dotnet test TacticSim.sln -c Release --no-build
docker build --build-arg SOURCE_REVISION="$(git rev-parse HEAD)" -t tacticsim:release0 .
python3 scripts/verify_deployment.py tacticsim:release0
```

The Docker check creates its own PostgreSQL 17 container/network, applies the
migrations twice, exercises a complete game through a temporary locally trusted
TLS edge, verifies cookies/CSRF/two-owner isolation/concurrency/history/resume,
replaces the web container, retries an old attack, and checks database outage/
recovery readiness. It removes only its own temporary resources. It is local
production-container evidence, **not a check of a Render deployment**. Browser
inspection remains separate. CI also runs this check on Linux.

Official references: [Render Docker](https://render.com/docs/docker),
[port binding](https://render.com/docs/web-services#port-binding),
[pre-deploy commands](https://render.com/docs/deploys#pre-deploy-command),
[Blueprint fields](https://render.com/docs/blueprint-spec),
[EF Data Protection storage](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0#persist-keys-in-a-database-persistkeystodbcontext),
[forwarded headers](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0).
