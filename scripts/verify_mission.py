"""Play Rivergate with real HTTP commands. Uses only this script's anonymous matches.
Start Web first; pass its origin as the first argument. No database edits/cheats.
"""
import json
import uuid
import verify_web as web


def main():
    browser = web.Browser()
    state = browser.command(None, "new")
    assert state["mission"]["type"] == "Rivergate"
    def move(unit, x, y):
        nonlocal state
        state = browser.command(state, "move", unitId=unit, x=x, y=y)
    def attack(unit, target):
        nonlocal state
        state = browser.command(state, "attack", unitId=unit, targetId=target)
    def end_round():
        nonlocal state
        state = browser.command(state, "endTurn")

    # A reproducible tactical route, also tested against Core. The opponent
    # contests F4 and punishes exposed units; this is deliberately not a perfect score.
    move(3, 2, 1); move(2, 5, 0); move(1, 3, 0); end_round()
    move(2, 5, 1); move(1, 5, 0); move(3, 3, 1); end_round()
    assert any(u["side"] == "Computer" and u["health"] > 0 and (u["x"], u["y"]) == (5, 3) for u in state["units"])
    assert state["mission"]["heldRounds"] == 0
    move(2, 5, 2); move(1, 6, 2); attack(1, 4); move(3, 4, 0); attack(3, 5); attack(2, 5); end_round()
    attack(2, 5); move(3, 6, 0); attack(3, 4); end_round()
    attack(2, 4); move(3, 6, 2); attack(3, 6); end_round()
    attack(3, 6); end_round()
    assert all(u["health"] == 0 for u in state["units"] if u["side"] == "Computer")
    assert state["winner"] is None, "Elimination alone must not win Rivergate"
    move(3, 5, 3)
    first_round = web.command_payload(state, "endTurn")
    status, response, _ = browser.request("/?handler=Command", first_round)
    assert status == 200
    state = response["state"]
    assert state["mission"]["heldRounds"] == 1 and state["mission"]["lastEvaluatedComputerTurn"] == 14
    assert browser.request("/?handler=Command", first_round)[1]["state"] == state
    assert browser.request("/?handler=Command", {**first_round, "commandId": str(uuid.uuid4())})[0] == 409
    browser.request("/"); assert browser.state() == state
    held = state
    state = browser.command(state, "new")
    state = browser.command(state, "resume", targetMatchId=held["matchId"])
    assert state["mission"] == held["mission"] and state["units"] == held["units"]
    status, review, _ = browser.request("/History?matchId=" + held["matchId"])
    assert status == 200 and "Hold the outpost: 1/2 rounds" in review
    end_round()
    assert state["winner"] == "Human" and state["mission"]["heldRounds"] == 2
    assert state["mission"]["lastEvaluatedComputerTurn"] == 16
    assert state["mission"]["conclusion"].startswith("Outpost secured")
    report = state["report"]
    assert report["outcome"] == "Mission success" and report["completedRounds"] == 8
    assert (report["friendlySurvivors"], report["friendlyTotal"], report["opposingSurvivors"]) == (1, 3, 0)
    assert report["captureResult"] == "Secured · 2/2 rounds"
    assert report["roundDescription"] == "8 completed rounds."
    assert sum(u["health"] == 0 for u in state["units"] if u["side"] == "Human") == 2
    assert sum("Outpost held after completed round" in e["text"] for e in state["history"]) == 2
    print("PASS: real Rivergate victory, enemy contests F4/two friendly losses, elimination requires capture, 0→1→2 after computer rounds")
    print("PASS: duplicate/stale round commands, refresh at 1/2, New Match/Resume, history and terminal mission explanation")

    # A reckless push without attacks demonstrates the failure path with the
    # same server-provided legal choices. This strategy does not compute damage.
    loser = web.Browser(); lost = loser.command(None, "new")
    for _ in range(25):
        if lost["winner"]: break
        for uid in [3, 2, 1]:
            unit = next(u for u in lost["units"] if u["id"] == uid)
            if unit["health"] == 0 or not unit["legalMoves"]: continue
            goal = lost["mission"]
            if (unit["x"], unit["y"]) == (goal["x"], goal["y"]): continue
            step = min(unit["legalMoves"], key=lambda m: (abs(m["x"] - goal["x"]) + abs(m["y"] - goal["y"]), m["cost"], m["y"], m["x"]))
            lost = loser.command(lost, "move", unitId=uid, x=step["x"], y=step["y"])
        lost = loser.command(lost, "endTurn")
    assert lost["winner"] == "Computer", "Reckless example should produce a real defeat"
    assert lost["mission"]["heldRounds"] == 0 and lost["mission"]["conclusion"] == "Your squad was eliminated."
    assert all(u["health"] == 0 for u in lost["units"] if u["side"] == "Human")
    assert lost["report"]["completedRounds"] == (lost["turnNumber"] - 1) // 2
    assert lost["report"]["friendlySurvivors"] == 0 and lost["report"]["outcome"] == "Mission failed"
    assert "Ended during round" in lost["report"]["roundDescription"]
    print("PASS: reckless advance loses the entire squad; mission failure resets capture and locks actions")
    print(json.dumps({"winningMatch": state["matchId"], "winningTurn": state["turnNumber"], "losingMatch": lost["matchId"], "losingTurn": lost["turnNumber"]}))


if __name__ == "__main__":
    main()
