# Portfolio demo and CV text

## 60-second demo

Preparation: wake the free host before presenting. Keep one unfinished match
ready at 1/2 capture in the **same browser**, plus an earlier completed result in
history. Use ordinary gameplay to prepare it; do not alter database rows or fake
statistics. If a saved demonstration is unavailable, show initial moves and
state that completing a mission takes longer than this one-minute overview.

| Time | Show | Say |
| --- | --- | --- |
| 0–10 s | Briefing and real-map preview | “TacticSim is a small fictional tactical game. I command three blue units and need to hold F4 through two completed rounds.” |
| 10–25 s | Begin, select Infantry and make a highlighted move | “Movement uses terrain-aware Dijkstra search in C#. The browser receives legal moves; it does not decide legality or damage.” |
| 25–38 s | End Turn; point to ordered opponent response and Skip | “The opponent is deterministic. These short effects present accepted server events in order, and I can skip them.” |
| 38–50 s | History → Resume the prepared 1/2 match; End Turn | “PostgreSQL preserves positions, health, actions and capture progress. Here the second held round completes the mission.” |
| 50–60 s | Actual report; mention the test suite/source | “The report uses recorded state: completed rounds, survivors and capture result. Owner checks, antiforgery, versions and idempotent receipts protect commands.” |

Do not describe the action log as a replay or claim realistic military modeling.
Use the actual visible result, not a promised number of survivors or rounds.

## Factual CV description

**TacticSim: Operation Rivergate — C#/.NET portfolio project**

Built a turn-based tactical game with ASP.NET Core Razor Pages, plain
JavaScript/CSS and original SVG assets. Implemented server-authoritative game
rules, Dijkstra movement, deterministic combat/opponent behavior and a
capture-and-hold mission. Used EF Core/Npgsql with relational PostgreSQL saves,
ordered events, anonymous-session ownership, antiforgery, optimistic concurrency
and idempotent commands; added xUnit/PostgreSQL integration coverage and Docker/
GitHub Actions deployment checks.

Only add “deployed on Render with Prisma Postgres” when referring to the hosted
version recorded in the release checklist. This description makes no user-count,
performance, commercial-impact or simulation-accuracy claims.
