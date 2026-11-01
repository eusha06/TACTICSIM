using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TacticSim.Web.Services;

namespace TacticSim.Web.Pages;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class IndexModel(BrowserOwner owner, MatchService matches, ILogger<IndexModel> logger) : PageModel
{
    public void OnGet() => owner.EnsureCookie(HttpContext);

    public Task<IActionResult> OnGetStateAsync(CancellationToken ct) => WithOwner(async id =>
        new JsonResult(new { state = await matches.Read(id, ct) }));

    // Razor Pages automatically validates antiforgery on POST handlers.
    public Task<IActionResult> OnPostCommandAsync([FromBody] MatchCommand? command, CancellationToken ct) =>
        WithOwner(async id =>
        {
            if (!ModelState.IsValid || command is null)
                return new JsonResult(new { error = "Send a valid JSON command." }) { StatusCode = 400 };
            var reply = await matches.Execute(id, command, ct);
            return new JsonResult(new { state = reply.State, error = reply.Error }) { StatusCode = reply.Status };
        });

    private async Task<IActionResult> WithOwner(Func<Guid, Task<IActionResult>> action)
    {
        var id = owner.Read(HttpContext);
        if (id is null)
            return new JsonResult(new { error = "Your browser session expired. Reload this page to continue." }) { StatusCode = 401 };
        try { return await action(id.Value); }
        catch (Exception exception) when (exception is NpgsqlException or DbUpdateException)
        {
            logger.LogError(exception, "Could not access match storage");
            return new JsonResult(new { error = "Match storage is unavailable. Check PostgreSQL and apply the database migrations, then retry." }) { StatusCode = 503 };
        }
    }
}
