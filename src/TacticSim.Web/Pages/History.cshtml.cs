using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TacticSim.Web.Services;

namespace TacticSim.Web.Pages;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class HistoryModel(BrowserOwner owner, MatchService matches, ILogger<HistoryModel> logger) : PageModel
{
    public IReadOnlyList<MatchSummary> Matches { get; private set; } = [];
    public MatchView? CurrentState { get; private set; }
    public MatchView? SelectedMatch { get; private set; }
    public string? ErrorMessage { get; private set; }
    [BindProperty] public Guid CommandId { get; set; }
    [BindProperty] public long ExpectedVersion { get; set; }
    [BindProperty] public Guid? CurrentMatchId { get; set; }
    [BindProperty] public long? ExpectedMatchVersion { get; set; }
    [BindProperty] public Guid TargetMatchId { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid? matchId, CancellationToken ct)
    {
        var id = owner.Read(HttpContext);
        if (id is null)
        {
            owner.EnsureCookie(HttpContext);
            return Page();
        }
        try
        {
            if (matchId is Guid selected)
            {
                SelectedMatch = await matches.ReadMatch(id.Value, selected, ct);
                if (SelectedMatch is null) return NotFound();
            }
            await Load(id.Value, ct);
            return Page();
        }
        catch (Exception ex) when (ex is NpgsqlException or DbUpdateException)
        {
            return StorageError(ex);
        }
    }

    public async Task<IActionResult> OnPostResumeAsync(CancellationToken ct)
    {
        var id = owner.Read(HttpContext);
        if (id is null) return Unauthorized();
        try
        {
            if (!ModelState.IsValid) return BadRequest();
            var reply = await matches.Execute(id.Value, new MatchCommand(CommandId, ExpectedVersion, "resume",
                MatchId: CurrentMatchId, ExpectedMatchVersion: ExpectedMatchVersion, TargetMatchId: TargetMatchId), ct);
            if (reply.Status == 200) return RedirectToPage("/Index");
            if (reply.Status == 404) return NotFound();
            ErrorMessage = reply.Error;
            Response.StatusCode = reply.Status;
            await Load(id.Value, ct);
            return Page();
        }
        catch (Exception ex) when (ex is NpgsqlException or DbUpdateException)
        {
            return StorageError(ex);
        }
    }

    private async Task Load(Guid id, CancellationToken ct)
    {
        Matches = await matches.History(id, ct);
        CurrentState = await matches.Read(id, ct);
    }

    private IActionResult StorageError(Exception exception)
    {
        logger.LogError(exception, "Could not access match history");
        Response.StatusCode = 503;
        ErrorMessage = "Match history is unavailable. Check PostgreSQL and apply the database migrations, then refresh.";
        return Page();
    }
}
