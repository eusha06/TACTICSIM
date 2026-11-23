using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using TacticSim.Sim;
using TacticSim.Sim.Ai;
using TacticSim.Web.Services;

namespace TacticSim.Persistence.Tests;

public sealed class V2ServiceTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static readonly GameContent C = GameContent.Load();

    private async Task<T> With<T>(Func<MatchV2Service, Task<T>> action)
    {
        await using var db = fixture.Open();
        return await action(new MatchV2Service(db));
    }

    private Task<V2Reply> Create(Guid owner, string scenario = "rivergate", string difficulty = "Veteran") =>
        With(s => s.Create(owner, scenario, difficulty, CancellationToken.None));

    private Task<V2Reply> Submit(Guid owner, Guid match, long version, Order[] orders, Guid? command = null) =>
        With(s => s.SubmitTurn(owner, match, new TurnRequest(command ?? Guid.NewGuid(), version, orders), CancellationToken.None));

    private static Order[] BlueAi(TacticSim.Sim.MatchView state, int seed = 9) => AiPlanner.Plan(C, state, Difficulty.Veteran, seed);
    private static string Json(object? value) => JsonSerializer.Serialize(value, SimJson.Options);

    [Fact]
    public async Task Create_returns_a_fog_filtered_start_and_rejects_bad_input()
    {
        var owner = Guid.NewGuid();
        var reply = await Create(owner);
        Assert.Equal(201, reply.Status);
        var payload = reply.Payload!;
        Assert.Equal(("rivergate", "Veteran", "v1", 1, 1L), (payload.Match.Scenario, payload.Match.Difficulty, payload.Match.RulesVersion, payload.State.Turn, payload.Match.Version));
        Assert.All(payload.State.Units, u => Assert.Equal(Side.Blue, u.Side));
        Assert.Equal(400, (await Create(owner, "moon")).Status);
        Assert.Equal("Difficulty must be Recruit, Veteran or Commander.", (await Create(owner, "rivergate", "God")).Error);
    }

    [Fact]
    public async Task Matches_belong_to_their_owner_only()
    {
        var owner = Guid.NewGuid();
        var id = (await Create(owner)).Payload!.Match.Id;
        var stranger = Guid.NewGuid();
        Assert.Equal(404, (await With(s => s.Get(stranger, id, default))).Status);
        Assert.Equal(404, (await With(s => s.GetTurn(stranger, id, 1, default))).Status);
        Assert.Equal(404, (await Submit(stranger, id, 1, [])).Status);
        Assert.Empty(await With(s => s.List(stranger, default)));
        Assert.Single(await With(s => s.List(owner, default)));
    }

    [Fact]
    public async Task A_turn_saves_both_order_sets_and_the_result_atomically()
    {
        var owner = Guid.NewGuid();
        var start = (await Create(owner)).Payload!;
        var reply = await Submit(owner, start.Match.Id, 1, BlueAi(start.State));
        Assert.Equal(200, reply.Status);
        Assert.Equal((2, 2L), (reply.Payload!.State.Turn, reply.Payload.Match.Version));
        Assert.Equal(9, reply.Payload.Replay!.Ticks.Length);
        Assert.NotEmpty(reply.Payload.Replay.Debrief);
        await using var db = fixture.Open();
        Assert.Equal(2, await db.TurnOrdersV2.CountAsync(o => o.MatchId == start.Match.Id && o.Turn == 1));
        Assert.Equal(1, await db.TurnResultsV2.CountAsync(r => r.MatchId == start.Match.Id));
        Assert.Equal(Json(reply.Payload.State), Json((await With(s => s.Get(owner, start.Match.Id, default))).Payload!.State));
    }

    [Fact]
    public async Task Illegal_orders_are_rejected_with_the_engine_reason_and_nothing_is_saved()
    {
        var owner = Guid.NewGuid();
        var id = (await Create(owner)).Payload!.Match.Id;
        var reply = await Submit(owner, id, 1, [new Order(5, OrderType.Attack)]);
        Assert.Equal((422, "Attack needs a target: pick a visible enemy.", 5), (reply.Status, reply.Error!, reply.UnitId!.Value));
        Assert.NotNull(reply.Payload);
        Assert.Equal("You can only give orders to your own units.", (await Submit(owner, id, 1, [Order.Hold(7)])).Error);
        await using var db = fixture.Open();
        Assert.False(await db.TurnOrdersV2.AnyAsync(o => o.MatchId == id));
    }

    [Fact]
    public async Task Retrying_a_command_returns_the_saved_result_without_replaying_it()
    {
        var owner = Guid.NewGuid();
        var start = (await Create(owner)).Payload!;
        var command = Guid.NewGuid();
        var orders = BlueAi(start.State);
        var first = await Submit(owner, start.Match.Id, 1, orders, command);
        var retry = await Submit(owner, start.Match.Id, 1, orders, command);
        Assert.Equal(200, retry.Status);
        Assert.Equal(Json(first.Payload!.Replay), Json(retry.Payload!.Replay));
        Assert.Equal(2L, retry.Payload.Match.Version);
        var reused = await Submit(owner, start.Match.Id, 2, [], command);
        Assert.Equal((409, "This command ID was already used for different orders. Reload and try again."), (reused.Status, reused.Error!));
        await using var db = fixture.Open();
        Assert.Equal(1, await db.TurnResultsV2.CountAsync(r => r.MatchId == start.Match.Id));
    }

    [Fact]
    public async Task Stale_and_concurrent_submissions_get_a_conflict()
    {
        var owner = Guid.NewGuid();
        var id = (await Create(owner)).Payload!.Match.Id;
        var stale = await Submit(owner, id, 7, []);
        Assert.Equal(409, stale.Status);
        Assert.Equal(1L, stale.Payload!.Match.Version);

        var replies = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Submit(owner, id, 1, [])));
        Assert.Equal(1, replies.Count(r => r.Status == 200));
        Assert.All(replies.Where(r => r.Status != 200), r => Assert.Equal(409, r.Status));
        await using var db = fixture.Open();
        Assert.Equal(1, await db.TurnResultsV2.CountAsync(r => r.MatchId == id));
    }

    [Fact]
    public async Task Responses_never_contain_hidden_enemies_and_replays_reproduce_every_snapshot()
    {
        var owner = Guid.NewGuid();
        var payload = (await Create(owner, difficulty: "Commander")).Payload!;
        var id = payload.Match.Id;
        for (var turn = 1; turn <= 5 && payload.Match.Status == "active"; turn++)
        {
            var reply = await Submit(owner, id, payload.Match.Version, BlueAi(payload.State, turn));
            Assert.Equal(200, reply.Status);
            payload = reply.Payload!;
            await using var db = fixture.Open();
            var stored = await db.TurnResultsV2.SingleAsync(r => r.MatchId == id && r.Turn == turn);
            var raw = JsonSerializer.Deserialize<JsonElement>(stored.Replay).GetProperty("ticks");
            var snapshot = JsonSerializer.Deserialize<MatchState>(stored.EndSnapshot, SimJson.Options)!;
            var red = snapshot.Units.Where(u => u.Side == Side.Red).Select(u => u.Id).ToHashSet();
            foreach (var tick in payload.Replay!.Ticks)
            {
                var sees = raw.EnumerateArray().Single(t => t.GetProperty("tick").GetInt32() == tick.Tick)
                    .GetProperty("blueSees").EnumerateArray().Select(e => e.GetInt32()).ToHashSet();
                var leaks = tick.Events.Where(e => e.Kind != EventKind.Lost && new[] { e.UnitId, e.TargetId }.OfType<int>().Any(i => red.Contains(i) && !sees.Contains(i)))
                    .Select(e => $"tick {tick.Tick} {e.Kind}").Concat(tick.Units.Where(u => red.Contains(u.Id) && !sees.Contains(u.Id)).Select(u => $"tick {tick.Tick} frame {u.Id}"));
                // A "lost" event names an enemy the viewer saw a moment ago; anything else must be in sight.
                Assert.Empty(leaks);
            }
            var visible = Vision.ViewOf(C, snapshot, Side.Blue).VisibleEnemies.Select(u => u.Id).ToHashSet();
            Assert.All(payload.State.Units.Where(u => u.Side == Side.Red), u => Assert.Contains(u.Id, visible));
            Assert.DoesNotContain("\"orders\"", Json(payload));
        }
        Assert.True(await With(s => s.ReplayMatches(id, default)));
    }

    [Fact]
    public async Task A_finished_boot_camp_is_saved_as_complete_and_takes_no_more_turns()
    {
        var owner = Guid.NewGuid();
        var payload = (await Create(owner, "bootcamp", "Recruit")).Payload!;
        while (payload.Match.Status == "active")
            payload = (await Submit(owner, payload.Match.Id, payload.Match.Version, BlueAi(payload.State))).Payload!;
        Assert.NotNull(payload.Match.CompletedAt);
        Assert.True(payload.State.FogLifted);
        var after = await Submit(owner, payload.Match.Id, payload.Match.Version, []);
        Assert.Equal((409, "This match is over. Start a new one from the menu."), (after.Status, after.Error!));
    }

    [Fact]
    public async Task Classic_v1_matches_still_load_next_to_v2_matches()
    {
        var owner = Guid.NewGuid();
        await using (var db = fixture.Open())
            Assert.Equal(200, (await new MatchService(db).Execute(owner, new MatchCommand(Guid.NewGuid(), 0, "new"), default)).Status);
        await Create(owner);
        await using var read = fixture.Open();
        Assert.Single(await new MatchService(read).History(owner, default));
        Assert.NotNull(await new MatchService(read).Read(owner, default));
        Assert.Single(await new MatchV2Service(read).List(owner, default));
    }
}

/// <summary>The real app in memory: owner cookie and antiforgery checks on /api/v2.</summary>
public sealed class V2HttpTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private HttpClient Client() => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
    {
        b.UseEnvironment("Development");
        b.UseSetting("ConnectionStrings:DefaultConnection", fixture.ConnectionString);
    }).CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private static async Task<string> Token(HttpClient client)
    {
        var page = await client.GetStringAsync("/");
        return WebUtility.HtmlDecode(Regex.Match(page, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value);
    }

    [Fact]
    public async Task Rules_are_public_but_matches_need_the_owner_cookie()
    {
        using var client = Client();
        var rules = await client.GetFromJsonAsync<JsonElement>("/api/v2/rules");
        Assert.Equal("v1", rules.GetProperty("rulesVersion").GetString());
        Assert.Equal(5, rules.GetProperty("units").GetArrayLength());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v2/matches")).StatusCode);
    }

    [Fact]
    public async Task Posts_without_the_antiforgery_header_are_refused()
    {
        using var client = Client();
        var token = await Token(client);
        var bare = await client.PostAsJsonAsync("/api/v2/matches", new { scenario = "rivergate" });
        Assert.Equal(HttpStatusCode.BadRequest, bare.StatusCode);
        Assert.Contains("security token", await bare.Content.ReadAsStringAsync());

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v2/matches") { Content = JsonContent.Create(new { scenario = "bootcamp", difficulty = "recruit" }) };
        request.Headers.Add("X-CSRF-TOKEN", token);
        var created = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("match").GetProperty("id").GetString();
        Assert.Equal("blue", body.GetProperty("state").GetProperty("units")[0].GetProperty("side").GetString());

        var turn = new HttpRequestMessage(HttpMethod.Post, $"/api/v2/matches/{id}/turns")
        {
            Content = new StringContent($$"""{"commandId":"{{Guid.NewGuid()}}","expectedVersion":1,"orders":[{"unitId":1,"type":"move","path":[{"col":2,"row":4}]}]}""",
                System.Text.Encoding.UTF8, "application/json")
        };
        turn.Headers.Add("X-CSRF-TOKEN", token);
        var played = await client.SendAsync(turn);
        Assert.Equal(HttpStatusCode.OK, played.StatusCode);
        var replay = (await played.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replay");
        Assert.Equal(9, replay.GetProperty("ticks").GetArrayLength());

        using var stranger = Client();
        await Token(stranger);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/v2/matches/{id}")).StatusCode);
        var list = await client.GetFromJsonAsync<JsonElement>("/api/v2/matches");
        Assert.Equal(1, list.GetProperty("matches").GetArrayLength());
    }
}
