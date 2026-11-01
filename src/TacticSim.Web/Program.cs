using Microsoft.EntityFrameworkCore;
using Npgsql;
using TacticSim.Web.Data;
using TacticSim.Web.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages();
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddSingleton<BrowserOwner>();
builder.Services.AddScoped<MatchService>();
var connection = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connection) && builder.Environment.IsDevelopment())
    connection = new NpgsqlConnectionStringBuilder
    {
        Host = "/tmp", Database = "tacticsim", Username = Environment.UserName
    }.ConnectionString;
if (string.IsNullOrWhiteSpace(connection))
    throw new InvalidOperationException("Set ConnectionStrings__DefaultConnection for PostgreSQL.");
builder.Services.AddDbContext<MatchDbContext>(options => options.UseNpgsql(connection));

var app = builder.Build();
if (args.Contains("--migrate"))
{
    // Schema changes and the legacy snapshot import are versioned migrations.
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<MatchDbContext>().Database.MigrateAsync();
    Console.WriteLine("TacticSim migrations applied.");
    return;
}
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseRouting();
app.UseAuthorization();
app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();
app.Run();
