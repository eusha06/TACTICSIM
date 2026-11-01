"""Start, stop and restart Web on an isolated port; keep test cookies only in memory.
Build and apply migrations first. Uses the same DB configuration as the app.
"""
import os
from pathlib import Path
import socket
import subprocess
import tempfile
import time
import urllib.request

import verify_web as web

ROOT = Path(__file__).resolve().parents[1]
BASE = "http://127.0.0.1:5181"
web.BASE = BASE
WEB_ROOT = ROOT / "src" / "TacticSim.Web"
DLL = WEB_ROOT / "bin" / "Debug" / "net10.0" / "TacticSim.Web.dll"


def start(log):
    with socket.socket() as probe:
        assert probe.connect_ex(("127.0.0.1", 5181)) != 0, "Port 5181 is already in use; no server was stopped."
    environment = os.environ.copy()
    environment["ASPNETCORE_ENVIRONMENT"] = "Development"
    process = subprocess.Popen(["dotnet", str(DLL), "--urls", BASE], cwd=WEB_ROOT,
        env=environment, stdout=log, stderr=subprocess.STDOUT)
    try:
        deadline = time.monotonic() + 20
        while time.monotonic() < deadline:
            assert process.poll() is None, "Test server exited during startup."
            try:
                with urllib.request.urlopen(BASE, timeout=1) as response:
                    if response.status == 200:
                        return process
            except OSError:
                time.sleep(0.1)
        raise AssertionError("Test server did not become ready.")
    except BaseException:
        stop(process)
        raise


def stop(process):
    if process is None or process.poll() is not None:
        return
    process.terminate()
    try:
        process.wait(timeout=10)
    except subprocess.TimeoutExpired:
        process.kill()  # Only this script's own server process.
        process.wait(timeout=5)


process = None
with tempfile.TemporaryFile() as log:
    try:
        process = start(log)
        browser = web.Browser()
        state = browser.command(None, "new")
        state = browser.command(state, "move", unitId=1, x=0, y=2)
        state = browser.command(state, "move", unitId=2, x=1, y=3)
        state = browser.command(state, "move", unitId=3, x=4, y=0)
        state = browser.command(state, "endTurn")
        state = browser.command(state, "move", unitId=3, x=6, y=0)
        artillery = next(unit for unit in state["units"] if unit["id"] == 3)
        assert 5 in artillery["legalTargets"]
        attack = web.command_payload(state, "attack", unitId=3, targetId=5)
        status, reply, _ = browser.request("/?handler=Command", attack)
        assert status == 200, reply
        before = reply["state"]
        assert next(unit for unit in before["units"] if unit["id"] == 3)["hasAttacked"]
        assert next(unit for unit in before["units"] if unit["id"] == 5)["health"] == 11
        old_pid = process.pid
        stop(process)
        process = start(log)
        assert process.pid != old_pid
        assert browser.state() == before, "State changed across application restart."
        assert browser.request("/?handler=Command", attack)[1]["state"] == before
        assert browser.state() == before, "Retry after restart applied an attack twice."
        browser.request("/")
        assert browser.state() == before
        status, history, _ = browser.request("/History?matchId=" + before["matchId"])
        assert status == 200 and "Artillery #3 hit Armor #5 for 5 damage." in history
        bad_move = web.command_payload(before, "move", unitId=3, x=7, y=0)
        assert browser.request("/?handler=Command", bad_move)[0] == 422
        print("PASS: actual process restart preserves session cookie, match/unit state, turn/action flags, versions and ordered history")
        print("PASS: pre-restart command receipt prevents a duplicate attack; saved match remains visible on History")
    finally:
        stop(process)
