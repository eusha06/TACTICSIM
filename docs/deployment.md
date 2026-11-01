# Release 0 deployment

No existing hosting service or production URL was identified. `render.yaml` is a
prepared deployment definition, not evidence of a live deployment. It uses the
existing Razor Pages app, .NET 10 Docker runtime, and an explicitly supplied
PostgreSQL database. It creates no database and changes no game rules.

## Render account steps

1. Sign in to the intended Render workspace. If a TacticSim service already
   exists, retain its database, hostname, secrets, keys, and service; adapt that
   service instead of creating a replacement. Record its service ID and URL.
2. Identify the intended durable PostgreSQL database. For a new installation,
   provision managed PostgreSQL with backups and a retention plan suitable for
   saved games; do not rely on an expiring disposable database. Prefer the same
   Render region/private network as the web service. Existing data must be moved
   using a verified backup/restore if changing providers, never by starting with
   an empty replacement. Keep the original database/backups intact.
3. Create a Blueprint from `eusha06/TACTICSIM`, `main`, repository-root
   `render.yaml`. It specifies a **paid Starter web service** because the
   pre-deploy migration hook requires paid compute. Review the account's current
   pricing before creation. No purchase was performed by this release.
4. Set secret **`ConnectionStrings__DefaultConnection`** in Render's environment
   to an **Npgsql key/value connection string**, not Render's `postgresql://` URL:

   ```text
   Host=<database-host>;Port=5432;Database=<database-name>;Username=<database-user>;Password=<secret>;SSL Mode=VerifyFull;GSS Encryption Mode=Disable
   ```

   Copy individual fields from the database's connection details. Use the TLS
   policy/certificate chain required by the actual database provider; do not
   disable certificate verification for an external connection. Password/TLS
   hosting disables optional Kerberos GSS encryption (the slim runtime has no
   Kerberos libraries); TLS remains enabled and verified. Keep the value
   in the provider's secret store, never Git, a Docker build argument, or logs.
   Restrict database access to the application/operator and migration role.
5. Set **`AllowedHosts`** to the service's actual assigned hostname (and any
   intended custom hostname, separated with `;`, without schemes/ports).
   Keep `PORT=10000`, `ASPNETCORE_ENVIRONMENT=Production`, and
   `ReverseProxy__Enabled=true` as defined in the Blueprint. No Docker start
   override is needed. Root Docker context and `./Dockerfile` are required.
6. Before initial creation/deployment, perform the backup/migration gates below.
   Keep automatic deploys off. The pre-deploy command is
   `dotnet TacticSim.Web.dll --migrate`; health path is `/health`. Deploy the exact
   pushed commit **after its CI checks pass**, then record the real assigned URL
   and Render deployment ID/commit. The initial Blueprint creation can start a
   deploy, so prepare backups/secrets before submitting it.
7. Compare `GET https://<actual-host>/health`'s `revision` with the full commit in
   Render's deployment details. Follow the deployed play checklist in
   `release-checklist.md`, then restart/redeploy the same service and repeat
   refresh/history/resume with the same browser cookie.

The account/service URL, production connection, backup confirmation, and
authenticated deployment access are required to perform these steps. They were
not available during local implementation; no deployment URL is invented here.

## Container and proxy behavior

The multi-stage Dockerfile restores/publishes only Core/Web with the .NET 10 SDK,
then runs the ASP.NET 10 runtime as its non-root `app` user. Its fixed working
directory is `/app`. No PostgreSQL files, dumps, key files, SDK, or test tooling
are copied into the runtime image. `.dockerignore` limits the source context.

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
4. After backup review, let Render's paid pre-deploy hook run the new image's
   `--migrate`. Failed migrations prevent promotion; inspect the error rather
   than dropping tables. Re-running an applied migration is a no-op.
5. Verify health, saved-match counts and old cookies, then allow traffic. This
   additive schema permits rolling the **application image** back while keeping
   the schema/keys. An old binary that uses filesystem keys still needs its old
   ring/discriminator; keep both backups. Any database restore requires a
   separate, deliberate recovery plan and write freeze, never automatic cleanup.

For a free Render web service without a pre-deploy hook, an operator must apply
the reviewed migration using the new published `--migrate` command from a trusted
machine with access to the database **before** deploying. This fallback is not
the supplied paid Blueprint; do not silently skip migrations.

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
