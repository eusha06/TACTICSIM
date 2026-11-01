"""Live HTTP checks. Start Web with a disposable local PostgreSQL database first.
Uses only Python's standard library; creates isolated anonymous test sessions.
"""
import concurrent.futures
import copy
import html
import http.cookiejar
import json
import re
import sys
import urllib.error
import urllib.request
import urllib.parse
import uuid

BASE = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:5178"
SSL_CONTEXT = None  # Optional trusted local test certificate; normal HTTPS validation by default.


def command_payload(state, kind, **fields):
    return {"commandId": str(uuid.uuid4()), "expectedVersion": state["version"] if state else 0,
        "matchId": state["matchId"] if state else None,
        "expectedMatchVersion": state["matchVersion"] if state else None, "kind": kind, **fields}


class Browser:
    def __init__(self, source=None):
        self.cookies = http.cookiejar.CookieJar()
        if source:
            for cookie in source.cookies:
                self.cookies.set_cookie(copy.copy(cookie))
        self.opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(self.cookies),
            urllib.request.HTTPSHandler(context=SSL_CONTEXT))
        if source:
            self.token = source.token
        else:
            status, page, _ = self.request("/")
            assert status == 200
            self.token = html.unescape(re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', page).group(1))

    def request(self, path, payload=None, csrf=True, form=False):
        headers = {}
        body = None
        if payload is not None:
            body = urllib.parse.urlencode({key: "" if value is None else value for key, value in payload.items()}).encode() if form else json.dumps(payload).encode()
            headers["Content-Type"] = "application/x-www-form-urlencoded" if form else "application/json"
            if csrf:
                headers["X-CSRF-TOKEN"] = self.token
        request = urllib.request.Request(BASE + path, data=body, headers=headers)
        try:
            response = self.opener.open(request, timeout=20)
        except urllib.error.HTTPError as error:
            response = error
        raw = response.read().decode()
        data = json.loads(raw) if "application/json" in response.headers.get("Content-Type", "") else raw
        return response.status, data, response.headers

    def state(self):
        status, body, _ = self.request("/?handler=State")
        assert status == 200, body
        return body["state"]

    def command(self, state, kind, **fields):
        payload = command_payload(state, kind, **fields)
        status, body, _ = self.request("/?handler=Command", payload)
        assert status == 200, (status, body)
        return body["state"]


def parallel_posts(browser, payloads):
    clients = [Browser(browser) for _ in payloads]
    with concurrent.futures.ThreadPoolExecutor(max_workers=len(payloads)) as pool:
        return list(pool.map(lambda pair: pair[0].request("/?handler=Command", pair[1]), zip(clients, payloads)))


def main():
    a, b = Browser(), Browser()
    assert a.state() is None and b.state() is None
    status, briefing, _ = a.request("/")
    assert status == 200 and 'id="welcome"' in briefing and 'Begin Operation' in briefing
    assert 'defeat the opposing squad' in briefing.lower() and 'id="help-dialog"' in briefing
    assert 'range 2–4 cells' in briefing and 'Terrain does not block line of sight' in briefing
    assert a.state() is None and a.request("/?handler=State")[1]["continueMatch"] is None
    assert a.request("/History")[0] == 200 and a.state() is None
    print("PASS: briefing/help/history reads leave first-visit match state empty")
    new = command_payload(None, "new")
    assert a.request("/?handler=Command", new, csrf=False)[0] == 400
    valid_token = a.token
    a.token = "invalid-token"
    assert a.request("/?handler=Command", new)[0] == 400
    a.token = valid_token
    assert a.state() is None
    status, data, _ = a.request("/?handler=Command", new)
    assert status == 200, data
    state = data["state"]
    assert state["version"] == 1 and len(state["units"]) == 6
    assert a.request("/?handler=Command", new)[1]["state"] == state
    assert a.request("/?handler=Command", {**new, "kind": "endTurn"})[0] == 409
    assert b.state() is None
    _, other_read, _ = b.request("/?handler=State&ownerId=" + str(uuid.uuid4()) + "&matchId=" + state["matchId"])
    assert other_read["state"] is None
    other = b.command(None, "new")
    assert other["matchId"] != state["matchId"]
    assert b.request("/?handler=Command", command_payload(other, "move", unitId=5, x=5, y=6, ownerId=state["matchId"]))[0] == 422
    assert a.state() == state
    tampered = Browser(a)
    for cookie in tampered.cookies:
        if cookie.name == "TacticSim.Browser":
            cookie.value = "forged-owner"
    assert tampered.request("/?handler=State")[0] == 401
    assert a.request("/?handler=Command", {"commandId": str(uuid.uuid4()), "expectedVersion": 0, "kind": "endTurn"})[0] == 409
    print("PASS: missing/invalid CSRF, isolated owners, forged-cookie rejection, opponent control, idempotent retry, stale version")

    same = command_payload(state, "endTurn")
    replies = parallel_posts(a, [same, same])
    assert [reply[0] for reply in replies] == [200, 200], replies
    assert replies[0][1]["state"] == replies[1][1]["state"]
    state = a.state()
    assert state["version"] == 2
    assert any(entry["side"] == "Computer" and "moved" in entry["text"] for entry in state["history"])
    choices = state["units"][0]["legalMoves"][:2]
    races = [command_payload(state, "move", unitId=1, x=move["x"], y=move["y"]) for move in choices]
    replies = parallel_posts(a, races)
    assert sorted(reply[0] for reply in replies) == [200, 409], replies
    state = a.state()
    assert state["version"] == 3
    print("PASS: concurrent duplicate applies once; different concurrent commands conflict atomically")

    before = state
    bad = command_payload(state, "move", unitId=2, x=-1, y=0)
    assert a.request("/?handler=Command", bad)[0] == 422
    assert a.state() == before
    # A page reload keeps the same server-recognized owner and saved state.
    a.request("/")
    assert a.state() == before
    print("PASS: invalid action does not mutate state; reload resumes the current match")

    # Retain an unfinished operation, then finish a separate one. Continue must
    # find the owned older save even when the active operation is completed.
    parked = state
    state = a.command(state, "new")
    assert a.request("/?handler=State")[1]["continueMatch"]["matchId"] == state["matchId"]
    # Choose human intents ONLY from the authoritative server-provided action lists.
    # This test strategy has no damage, terrain, or legal-movement implementation.
    attacks = 0
    for _ in range(80):
        if state["winner"]:
            break
        for unit_id in [1, 2, 3]:
            unit = next(u for u in state["units"] if u["id"] == unit_id)
            if unit["health"] == 0 or state["winner"]:
                continue
            enemies = [u for u in state["units"] if u["side"] == "Computer" and u["health"] > 0]
            if not unit["legalTargets"] and unit["legalMoves"]:
                def preference(cell):
                    distances = [abs(cell["x"] - enemy["x"]) + abs(cell["y"] - enemy["y"]) for enemy in enemies]
                    band = min(max(unit["minimumRange"] - distance, distance - unit["maximumRange"], 0) for distance in distances)
                    return band, min(distances), cell["cost"], cell["y"], cell["x"]
                move = min(unit["legalMoves"], key=preference)
                state = a.command(state, "move", unitId=unit_id, x=move["x"], y=move["y"])
                unit = next(u for u in state["units"] if u["id"] == unit_id)
            if unit["legalTargets"]:
                state = a.command(state, "attack", unitId=unit_id, targetId=unit["legalTargets"][0])
                attacks += 1
        if not state["winner"]:
            state = a.command(state, "endTurn")
    assert state["winner"] is not None, "Match did not finish in 80 rounds"
    assert attacks > 0, "Human strategy never attacked"
    assert state["results"] and state["results"][0]["winner"] == state["winner"]
    assert all(not unit["legalMoves"] and not unit["legalTargets"] for unit in state["units"])
    assert a.request("/?handler=Command", command_payload(state, "endTurn"))[0] == 422
    assert a.state() == state
    finished = state
    assert a.request("/?handler=State")[1]["continueMatch"]["matchId"] == parked["matchId"]
    assert b.request("/?handler=State")[1]["continueMatch"]["matchId"] == other["matchId"]
    assert a.state() == finished
    print("PASS: Continue candidate is owned/read-only and finds an older unfinished match after completion")
    state = a.command(state, "new")
    assert state["matchId"] != finished["matchId"] and state["version"] == finished["version"] + 1
    assert state["results"] == finished["results"]
    assert b.state() == other
    print(f"PASS: complete human/computer match ({finished['winner']} won on turn {finished['turnNumber']}, {attacks} human attacks)")
    print("PASS: completed match locks actions; New Match preserves completed results; other browser unaffected")

    # New Match retains unfinished state, and History is an ownership-checked Razor page.
    first = a.command(state, "move", unitId=1, x=0, y=1)
    second = a.command(first, "new")
    status, page, _ = a.request("/History")
    assert status == 200
    assert first["matchId"] in page and second["matchId"] in page and finished["matchId"] in page
    assert other["matchId"] not in page
    status, detail, _ = a.request("/History?matchId=" + first["matchId"])
    assert status == 200 and 'data-sequence="2"' in detail
    assert b.request("/History?matchId=" + first["matchId"])[0] == 404
    foreign = {"CommandId": str(uuid.uuid4()), "ExpectedVersion": other["version"],
        "CurrentMatchId": other["matchId"], "ExpectedMatchVersion": other["matchVersion"], "TargetMatchId": first["matchId"]}
    assert b.request("/History?handler=Resume", foreign, form=True)[0] == 404
    assert b.state() == other
    resume = {"CommandId": str(uuid.uuid4()), "ExpectedVersion": second["version"],
        "CurrentMatchId": second["matchId"], "ExpectedMatchVersion": second["matchVersion"], "TargetMatchId": first["matchId"]}
    assert a.request("/History?handler=Resume", resume, csrf=False, form=True)[0] == 400
    assert a.request("/History?handler=Resume", resume, form=True)[0] == 200
    resumed = a.state()
    assert resumed["matchId"] == first["matchId"] and resumed["units"][0]["hasMoved"]
    assert not resumed["units"][0]["legalMoves"]
    assert [entry["sequence"] for entry in resumed["history"]] == list(range(1, len(resumed["history"]) + 1))
    assert a.request("/History?handler=Resume", resume, form=True)[0] == 200
    assert a.state() == resumed
    assert a.request("/?handler=Command", command_payload(resumed, "move", unitId=1, x=0, y=2))[0] == 422
    print("PASS: session-owned history/review/resume, ordered events, CSRF on Resume, and preserved action limits")


if __name__ == "__main__":
    main()
