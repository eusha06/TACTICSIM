using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TacticSim.Sim;

namespace TacticSim.Web.Services;

/// <summary>/api/v2 JSON endpoints. Owner cookie on every call; antiforgery header on every POST.</summary>
public static class ApiV2
{
    public sealed record CreateRequest(string? Scenario, string? Difficulty);

    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api/v2").AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            http.Response.Headers.CacheControl = "no-store";
            if (HttpMethods.IsPost(http.Request.Method))
            {
                try { await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http); }
                catch (AntiforgeryValidationException)
                {
                    return Error(400, "Your security token is missing or expired. Reload the page and try again.");
                }
            }
            try { return await next(context); }
            catch (Exception ex) when (ex is NpgsqlException or DbUpdateException or TimeoutException)
            {
                http.RequestServices.GetRequiredService<ILogger<MatchV2Service>>().LogError(ex, "v2 match storage unavailable");
                return Error(503, "The battlefield server is busy. Your saved turns are safe; retry in a moment.");
            }
        });

        api.MapGet("/rules", () =>
        {
            var content = GameContent.Load();
            return Ok(new
            {
                content.RulesVersion, content.Rules,
                Terrain = content.Terrain.Values, Units = content.Units.Values,
                Maps = content.Maps.Values.Select(m => new { m.Id, m.Name, m.Width, m.Height, m.Tiles, m.Objectives, m.TurnLimit, m.VpToWin })
            });
        });

        api.MapGet("/matches", (HttpContext http, MatchV2Service v2, MatchService v1, CancellationToken ct) =>
            WithOwner(http, async owner => Ok(new
            {
                Matches = await v2.List(owner, ct),
                // v1 "Classic" games stay readable (and reviewable on /History); they are never resumed here.
                Classic = await v1.History(owner, ct)
            })));

        api.MapPost("/matches", (HttpContext http, MatchV2Service v2, CancellationToken ct) =>
            WithOwner(http, async owner =>
            {
                var body = await Read<CreateRequest>(http, ct);
                return body is null ? Error(400, "Send {scenario, difficulty} as JSON.") : Reply(await v2.Create(owner, body.Scenario, body.Difficulty, ct));
            }));

        api.MapGet("/matches/{id:guid}", (Guid id, HttpContext http, MatchV2Service v2, CancellationToken ct) =>
            WithOwner(http, async owner => Reply(await v2.Get(owner, id, ct))));

        api.MapPost("/matches/{id:guid}/turns", (Guid id, HttpContext http, MatchV2Service v2, CancellationToken ct) =>
            WithOwner(http, async owner =>
            {
                var body = await Read<TurnRequest>(http, ct);
                return body is null ? Error(400, "Send {commandId, expectedVersion, orders} as JSON.") : Reply(await v2.SubmitTurn(owner, id, body, ct));
            }));

        api.MapGet("/matches/{id:guid}/turns/{turn:int}", (Guid id, int turn, HttpContext http, MatchV2Service v2, CancellationToken ct) =>
            WithOwner(http, async owner => Reply(await v2.GetTurn(owner, id, turn, ct))));
    }

    private static async Task<IResult> WithOwner(HttpContext http, Func<Guid, Task<IResult>> action)
    {
        var owner = http.RequestServices.GetRequiredService<BrowserOwner>().Read(http);
        return owner is null ? Error(401, "Your browser session expired. Reload the page to continue.") : await action(owner.Value);
    }

    private static async Task<T?> Read<T>(HttpContext http, CancellationToken ct) where T : class
    {
        try { return await http.Request.ReadFromJsonAsync<T>(SimJson.Options, ct); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return null; }
    }

    private static IResult Reply(V2Reply reply) => reply.Payload is null && reply.Error is not null
        ? Error(reply.Status, reply.Error)
        : Results.Json(new { reply.Payload?.Match, reply.Payload?.State, reply.Payload?.Replay, reply.Error, reply.UnitId },
            SimJson.Options, statusCode: reply.Status);

    private static IResult Ok(object value) => Results.Json(value, SimJson.Options);
    private static IResult Error(int status, string message) => Results.Json(new { error = message }, SimJson.Options, statusCode: status);
}
