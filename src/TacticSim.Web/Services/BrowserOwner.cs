using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace TacticSim.Web.Services;

public sealed class BrowserOwner(IDataProtectionProvider protection)
{
    private const string CookieName = "TacticSim.Browser";
    private readonly IDataProtector _protector = protection.CreateProtector("TacticSim.AnonymousOwner.v1");

    public Guid? Read(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName, out var cookie)) return null;
        try { return Guid.TryParse(_protector.Unprotect(cookie), out var id) ? id : null; }
        catch (CryptographicException) { return null; }
    }

    public void EnsureCookie(HttpContext context)
    {
        if (Read(context) is not null) return;
        context.Response.Cookies.Append(CookieName, _protector.Protect(Guid.NewGuid().ToString()), new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            IsEssential = true,
            MaxAge = TimeSpan.FromDays(30)
        });
    }
}
