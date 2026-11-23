using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TacticSim.Web.Services;

namespace TacticSim.Web.Pages;

/// <summary>Hosts the v2 canvas client. The game itself talks to /api/v2.</summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class PlayModel(BrowserOwner owner) : PageModel
{
    public void OnGet() => owner.EnsureCookie(HttpContext);
}
