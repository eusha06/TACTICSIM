using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TacticSim.Web.Services;
using TacticSim.Core;

namespace TacticSim.Web.Pages;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class IndexModel(BrowserOwner owner, MatchService matches, ILogger<IndexModel> logger) : PageModel
{
    public void OnGet() => owner.EnsureCookie(HttpContext);

    public IReadOnlyList<Unit> GuideUnits { get; } = Game.CreateDefault().Units.Where(u => u.Side == Side.Human).ToArray();
    public Terrain[] GuideTerrains { get; } = Enum.GetValues<Terrain>();
    public int Defense(Terrain terrain)
    {
        var map = BattleMap.Default;
        var position = Enumerable.Range(0, BattleMap.Size * BattleMap.Size)
            .Select(i => new Position(i % BattleMap.Size, i / BattleMap.Size))
            .First(p => map.GetTerrain(p) == terrain);
        return map.GetDefense(position);
    }

    public sealed record ContinueView(Guid MatchId, int TurnNumber, string MissionType = "Elimination");

    public Task<IActionResult> OnGetStateAsync(CancellationToken ct) => WithOwner(async id =>
    {
        var state = await matches.Read(id, ct);
        ContinueView? candidate = state is { Winner: null }
            ? new(state.MatchId, state.TurnNumber, state.Mission?.Type ?? "Elimination") : null;
        if (candidate is null)
        {
            var unfinished = (await matches.History(id, ct)).FirstOrDefault(m => m.Winner is null);
            if (unfinished is not null) candidate = new(unfinished.MatchId, unfinished.TurnNumber, unfinished.MissionType);
        }
        return new JsonResult(new { state, continueMatch = candidate });
    });

    // Razor Pages automatically validates antiforgery on POST handlers.
    public Task<IActionResult> OnPostCommandAsync([FromBody] MatchCommand? command, CancellationToken ct) =>
        WithOwner(async id =>
        {
            if (!ModelState.IsValid || command is null)
                return new JsonResult(new { error = "Send a valid JSON command." }) { StatusCode = 400 };
            var reply = await matches.Execute(id, command, ct);
            return new JsonResult(new { state = reply.State, error = reply.Error, presentation = reply.Presentation }) { StatusCode = reply.Status };
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
