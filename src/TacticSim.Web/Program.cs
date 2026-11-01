using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using TacticSim.Web.Data;
using TacticSim.Web.Services;

// Remove our operator switches before ASP.NET parses its key/value arguments.
// Otherwise a bare --migrate can swallow the following --environment switch.
var hostArguments = new List<string>();
var migrate = false;
string? importDirectory = null;
for (var index = 0; index < args.Length; index++)
{
    if (args[index] == "--migrate") migrate = true;
    else if (args[index] == "--import-keys")
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--"))
            throw new InvalidOperationException("Use --import-keys <directory> after applying migrations.");
        importDirectory = args[++index];
    }
    else hostArguments.Add(args[index]);
}
if (migrate && importDirectory is not null)
    throw new InvalidOperationException("Run --migrate and --import-keys separately.");
var builder = WebApplication.CreateBuilder(hostArguments.ToArray());
// Render supplies PORT. Explicit --urls/ASPNETCORE_URLS still take precedence.
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]) &&
    builder.Configuration["PORT"] is { Length: > 0 } port)
{
    if (!int.TryParse(port, out var number) || number is < 1 or > 65535)
        throw new InvalidOperationException("PORT must be between 1 and 65535.");
    builder.WebHost.UseUrls($"http://0.0.0.0:{number}");
}
// Only opt in on a host where the managed edge is the sole public ingress.
// We accept one scheme header, never a forwarded host or client identity.
var behindProxy = builder.Configuration.GetValue<bool>("ReverseProxy:Enabled");
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    if (behindProxy)
    {
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    }
});
builder.Services.AddHttpsRedirection(options => options.HttpsPort = 443);
builder.Services.AddRazorPages();
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddSingleton<BrowserOwner>();
builder.Services.AddScoped<MatchService>();
var connection = builder.Configuration.GetConnectionString("DefaultConnection");
// Transaction poolers are suitable for gameplay, but migrations need a direct
// connection for their session-level database lock. Existing setups can keep
// using only DefaultConnection; the override applies only to --migrate.
var migrationConnection = builder.Configuration.GetConnectionString("MigrationConnection");
if (migrate && !string.IsNullOrWhiteSpace(migrationConnection))
    connection = migrationConnection;
if (string.IsNullOrWhiteSpace(connection) && builder.Environment.IsDevelopment())
    connection = new NpgsqlConnectionStringBuilder
    {
        Host = "/tmp", Database = "tacticsim", Username = Environment.UserName
    }.ConnectionString;
if (string.IsNullOrWhiteSpace(connection))
    throw new InvalidOperationException("Set ConnectionStrings__DefaultConnection for PostgreSQL.");
builder.Services.AddDbContext<MatchDbContext>(options => options.UseNpgsql(connection));
var protection = builder.Services.AddDataProtection();
if (!builder.Environment.IsDevelopment())
    protection.PersistKeysToDbContext<MatchDbContext>();
// Leave the existing local discriminator unchanged. Container content root /app
// is stable across deploys. An existing host can retain its exact discriminator.
if (builder.Configuration["DataProtection:ApplicationName"] is { Length: > 0 } applicationName)
    protection.SetApplicationName(applicationName);
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("postgresql", timeout: TimeSpan.FromSeconds(5));

var app = builder.Build();
if (importDirectory is not null)
{
    using var scope = app.Services.CreateScope();
    var count = await KeyRingImport.Import(scope.ServiceProvider.GetRequiredService<MatchDbContext>(), importDirectory);
    Console.WriteLine($"Imported {count} missing Data Protection records; existing keys retained.");
    return;
}
if (migrate)
{
    // Schema changes and the legacy snapshot import are versioned migrations.
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<MatchDbContext>().Database.MigrateAsync();
    Console.WriteLine("TacticSim migrations applied.");
    return;
}
if (!app.Environment.IsDevelopment())
{
    if (behindProxy) app.UseForwardedHeaders();
    app.UseExceptionHandler("/Error");
    app.UseHsts();
    // Render probes over internal HTTP; readiness never issues cookies or mutates state.
    app.UseWhen(context => context.Request.Path != "/health", branch => branch.UseHttpsRedirection());
}
app.UseRouting();
app.UseAuthorization();
app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        var revision = app.Configuration["RENDER_GIT_COMMIT"] ??
            typeof(BrowserOwner).Assembly.GetCustomAttributes(false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
                .FirstOrDefault()?.InformationalVersion.Split('+').ElementAtOrDefault(1) ?? "unknown";
        await context.Response.WriteAsJsonAsync(new
        {
            status = report.Status == HealthStatus.Healthy ? "healthy" : "unhealthy",
            revision
        });
    }
});
app.Run();
