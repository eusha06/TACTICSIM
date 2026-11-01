"""Production-container checks with a disposable PostgreSQL and local TLS edge.

Build the image first: docker build -t tacticsim:release0 .
Run: python3 scripts/verify_deployment.py tacticsim:release0
Only containers/network named by this run are removed; no existing DB is used.
"""
import http.client
import http.server
import html
import json
import os
from pathlib import Path
import secrets
import re
import ssl
import subprocess
import sys
import tempfile
import threading
import time
import urllib.error
import urllib.request
import uuid

import verify_web as web
import verify_mission as mission


def docker(*arguments, **kwargs):
    return subprocess.check_output(["docker", *arguments], text=True, **kwargs).strip()


def wait_for(check, description):
    deadline = time.monotonic() + 60
    while time.monotonic() < deadline:
        try:
            if check():
                return
        except (OSError, subprocess.CalledProcessError, http.client.HTTPException):
            pass
        time.sleep(0.2)
    raise AssertionError("Timed out: " + description)


def main():
    image = sys.argv[1] if len(sys.argv) > 1 else "tacticsim:release0"
    expected_revision = sys.argv[2] if len(sys.argv) > 2 else None
    prefix = "tacticsim_verify_" + uuid.uuid4().hex[:12]
    database, application = prefix + "_db", prefix + "_app"
    backend = [None]
    edge = None
    password = secrets.token_urlsafe(24)
    # Password stays in this process / disposable Docker metadata, never in Git/output.
    environment = os.environ.copy()
    environment["POSTGRES_PASSWORD"] = password
    environment["ConnectionStrings__DefaultConnection"] = (
        f"Host={database};Database=tacticsim_verify;Username=postgres;Password={password};Timeout=3;Command Timeout=3;GSS Encryption Mode=Disable")

    def ready():
        connection = http.client.HTTPConnection("127.0.0.1", backend[0], timeout=8)
        try:
            connection.request("GET", "/health")
            response = connection.getresponse()
            return response.status, json.loads(response.read())
        finally:
            connection.close()

    def start_application():
        docker("run", "-d", "--name", application, "--network", prefix, "--memory", "512m",
            "-p", "127.0.0.1::10000", "-e", "PORT=10000", "-e", "ReverseProxy__Enabled=true",
            "-e", "AllowedHosts=localhost;127.0.0.1;play.example.test", "-e", "ConnectionStrings__DefaultConnection", image,
            env=environment)
        backend[0] = int(docker("port", application, "10000/tcp").rsplit(":", 1)[1])
        wait_for(lambda: ready()[0] == 200, "production readiness")
        if expected_revision:
            assert ready()[1]["revision"] == expected_revision, "Health revision does not match the built source."
        assert docker("exec", application, "id", "-u") != "0", "Web must run as non-root."

    class Edge(http.server.BaseHTTPRequestHandler):
        protocol_version = "HTTP/1.1"

        def forward(self):
            connection = http.client.HTTPConnection("127.0.0.1", backend[0], timeout=20)
            try:
                body = self.rfile.read(int(self.headers.get("Content-Length", "0")))
                headers = {key: value for key, value in self.headers.items()
                    if key.lower() not in ("connection", "x-forwarded-proto")}
                headers["X-Forwarded-Proto"] = "https"
                connection.request(self.command, self.path, body or None, headers)
                response = connection.getresponse()
                data = response.read()
                self.send_response(response.status)
                for key, value in response.getheaders():
                    if key.lower() not in ("connection", "transfer-encoding", "content-length"):
                        self.send_header(key, value)
                self.send_header("Content-Length", str(len(data)))
                self.end_headers()
                self.wfile.write(data)
            finally:
                connection.close()

        do_GET = forward
        do_POST = forward

        def log_message(self, *_):
            pass

    try:
        docker("network", "create", prefix)
        docker("run", "-d", "--name", database, "--network", prefix,
            "-e", "POSTGRES_DB=tacticsim_verify", "-e", "POSTGRES_PASSWORD", "postgres:17", env=environment)
        wait_for(lambda: docker("exec", database, "pg_isready", "-h", "127.0.0.1", "-U", "postgres", "-d", "tacticsim_verify",
            stderr=subprocess.DEVNULL).endswith("accepting connections"), "isolated PostgreSQL")
        # The exact free Render command must bootstrap a completely empty DB.
        start_application()
        # Reapplying must be safe and leave existing state/import backups alone.
        docker("run", "--rm", "--network", prefix, "-e", "ConnectionStrings__DefaultConnection", image,
            "dotnet", "TacticSim.Web.dll", "--migrate", env=environment)
        print("PASS: free Render start command migrates an empty database before readiness; reapplication is safe")
        migration_environment = environment.copy()
        migration_environment["ConnectionStrings__MigrationConnection"] = environment["ConnectionStrings__DefaultConnection"]
        migration_environment["ConnectionStrings__DefaultConnection"] = (
            "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Timeout=1;GSS Encryption Mode=Disable")
        docker("run", "--rm", "--network", prefix, "-e", "ConnectionStrings__DefaultConnection",
            "-e", "ConnectionStrings__MigrationConnection", image, "dotnet", "TacticSim.Web.dll", "--migrate",
            env=migration_environment)
        print("PASS: migration-only direct connection works independently of the runtime connection")
        connection = http.client.HTTPConnection("127.0.0.1", backend[0])
        connection.request("GET", "/")
        response = connection.getresponse()
        assert response.status == 307 and response.getheader("Location").startswith("https://")
        response.read()
        connection.close()
        with tempfile.TemporaryDirectory(prefix=prefix) as directory:
            cert, key = Path(directory) / "cert.pem", Path(directory) / "key.pem"
            subprocess.run(["openssl", "req", "-x509", "-newkey", "rsa:2048", "-nodes", "-days", "1",
                "-keyout", str(key), "-out", str(cert), "-subj", "/CN=localhost",
                "-addext", "subjectAltName=DNS:localhost"], check=True, capture_output=True)
            tls = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
            tls.load_cert_chain(cert, key)
            edge = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Edge)
            edge.socket = tls.wrap_socket(edge.socket, server_side=True)
            threading.Thread(target=edge.serve_forever, daemon=True).start()
            web.BASE = f"https://localhost:{edge.server_port}"
            web.SSL_CONTEXT = ssl.create_default_context(cafile=str(cert))
            browser = web.Browser()
            owner_cookie = next(cookie for cookie in browser.cookies if cookie.name == "TacticSim.Browser")
            assert owner_cookie.secure
            assert any(name.lower() == "httponly" for name in owner_cookie._rest)
            status, page, headers = browser.request("/")
            assert status == 200
            assets = [html.unescape(path) for path in re.findall(r'(?:src|href|data-art-url)="([^\"]+\.(?:css|js|svg)(?:\?[^\"]*)?)"', page)
                if path.startswith("/")]
            assert any("/js/site." in path for path in assets), "Board JavaScript is missing from the page."
            assert any("/js/tutorial." in path for path in assets), "Tutorial JavaScript is missing from the page."
            assert any("/art/battlefield.svg?v=" in path for path in assets), "Versioned battlefield artwork is missing from the page."
            for path in assets:
                assert browser.request(path)[0] == 200, "Published asset is unavailable: " + path
            # ASP.NET intentionally excludes localhost from HSTS; use a public-host
            # request through the simulated trusted edge to check production HSTS.
            connection = http.client.HTTPConnection("127.0.0.1", backend[0])
            connection.request("GET", "/", headers={"Host": "play.example.test", "X-Forwarded-Proto": "https"})
            response = connection.getresponse()
            assert response.status == 200 and response.getheader("Strict-Transport-Security")
            response.read()
            connection.close()
            print("PASS: Production PORT binding, non-root user, HTTP redirect, TLS proxy scheme, Secure/HttpOnly owner cookie and HSTS")
            web.main()
            mission.main()  # Capture, reset, defeat and saved progress through HTTP.
            state = browser.command(None, "new")
            state = browser.command(state, "move", unitId=1, x=0, y=2)
            state = browser.command(state, "move", unitId=2, x=1, y=3)
            state = browser.command(state, "move", unitId=3, x=4, y=0)
            state = browser.command(state, "endTurn")
            state = browser.command(state, "move", unitId=3, x=6, y=0)
            attack = web.command_payload(state, "attack", unitId=3, targetId=5)
            status, reply, _ = browser.request("/?handler=Command", attack)
            assert status == 200, reply
            before = reply["state"]
            assert next(unit for unit in before["units"] if unit["id"] == 5)["health"] == 11
            docker("rm", "-f", application)
            start_application()  # New container: no local keys can survive in its filesystem.
            assert browser.state() == before
            assert browser.request("/?handler=Command", attack)[1]["state"] == before
            assert browser.state() == before
            assert browser.request("/History?matchId=" + before["matchId"])[0] == 200
            browser.request("/")
            assert browser.state() == before
            assert browser.request("/?handler=Command", web.command_payload(before, "move", unitId=3, x=7, y=0))[0] == 422
            print("PASS: container replacement retains owner cookie, damaged health, action flags, exact state/history and duplicate attack receipt")
            docker("stop", database)
            assert ready()[0] == 503, "Database outage must fail readiness."
            docker("start", database)
            wait_for(lambda: ready()[0] == 200, "readiness after PostgreSQL recovery")
            assert browser.state() == before
            print("PASS: database outage returns 503; recovery restores readiness without losing the match")
    finally:
        if edge:
            edge.shutdown()
            edge.server_close()
        for name in (application, database):
            subprocess.run(["docker", "rm", "-f", "-v", name], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        subprocess.run(["docker", "network", "rm", prefix], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


if __name__ == "__main__":
    main()
