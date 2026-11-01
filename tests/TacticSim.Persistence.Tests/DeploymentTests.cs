using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TacticSim.Web.Data;
using TacticSim.Web.Services;

namespace TacticSim.Persistence.Tests;

public sealed class DeploymentTests
{
    [Fact]
    public async Task Existing_filesystem_keys_can_be_imported_without_changing_cookie_identity()
    {
        using var database = new PostgresFixture();
        var directory = Directory.CreateTempSubdirectory("tacticsim-key-test-");
        try
        {
            string cookie;
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDataProtection().PersistKeysToFileSystem(directory).SetApplicationName("original-discriminator");
            using (var oldHost = services.BuildServiceProvider())
                cookie = oldHost.GetRequiredService<IDataProtectionProvider>()
                    .CreateProtector("TacticSim.AnonymousOwner.v1").Protect(Guid.NewGuid().ToString());
            await using var db = database.Open();
            Assert.Equal(1, await KeyRingImport.Import(db, directory.FullName));
            Assert.Equal(0, await KeyRingImport.Import(db, directory.FullName));
            var newServices = new ServiceCollection();
            newServices.AddLogging();
            newServices.AddScoped(_ => database.Open());
            newServices.AddDataProtection().PersistKeysToDbContext<MatchDbContext>()
                .SetApplicationName("original-discriminator");
            using var newHost = newServices.BuildServiceProvider();
            var context = new DefaultHttpContext();
            context.Request.Headers.Cookie = "TacticSim.Browser=" + cookie;
            Assert.NotNull(new BrowserOwner(newHost.GetRequiredService<IDataProtectionProvider>()).Read(context));
            Assert.Single(await db.DataProtectionKeys.ToListAsync());
        }
        finally { directory.Delete(true); } // Only the temporary test ring, never existing application keys.
    }

    [Fact]
    public async Task Independent_application_instances_read_the_same_owner_cookie_from_PostgreSQL_keys()
    {
        using var database = new PostgresFixture();
        ServiceProvider Instance(string name)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddScoped(_ => database.Open());
            services.AddDataProtection().PersistKeysToDbContext<MatchDbContext>().SetApplicationName(name);
            services.AddSingleton<BrowserOwner>();
            return services.BuildServiceProvider();
        }
        string cookie;
        Guid owner;
        using (var first = Instance("deployment-test"))
        {
            var context = new DefaultHttpContext();
            context.Request.Scheme = "https";
            first.GetRequiredService<BrowserOwner>().EnsureCookie(context);
            var header = context.Response.Headers.SetCookie.ToString();
            Assert.Contains("secure", header);
            Assert.Contains("httponly", header);
            cookie = header.Split(';')[0];
            context.Request.Headers.Cookie = cookie;
            owner = first.GetRequiredService<BrowserOwner>().Read(context)!.Value;
        }
        using (var second = Instance("deployment-test"))
        {
            var context = new DefaultHttpContext();
            context.Request.Headers.Cookie = cookie;
            Assert.Equal(owner, second.GetRequiredService<BrowserOwner>().Read(context));
        }
        using (var otherApplication = Instance("different-application"))
        {
            var context = new DefaultHttpContext();
            context.Request.Headers.Cookie = cookie;
            Assert.Null(otherApplication.GetRequiredService<BrowserOwner>().Read(context));
        }
        await using var db = database.Open();
        Assert.NotEmpty(await db.DataProtectionKeys.ToListAsync());
        Assert.Empty(await db.Sessions.ToListAsync());
    }

    [Fact]
    public async Task Readiness_checks_schema_without_creating_sessions_or_keys()
    {
        using var database = new PostgresFixture();
        await using var db = database.Open();
        var result = await new DatabaseHealthCheck(db).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Empty(await db.Sessions.ToListAsync());
        Assert.Empty(await db.DataProtectionKeys.ToListAsync());
    }

    [Fact]
    public async Task Readiness_fails_when_the_required_key_table_is_missing()
    {
        using var database = new PostgresFixture();
        await database.Sql("DROP TABLE \"DataProtectionKeys\""); // Only this test's isolated schema.
        await using var db = database.Open();
        var result = await new DatabaseHealthCheck(db).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Null(result.Exception);
        Assert.Equal("PostgreSQL schema is unavailable.", result.Description);
    }
}
