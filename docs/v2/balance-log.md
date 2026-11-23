# Balance log

Runner: `dotnet run --project tools/TacticSim.Balance -c Release -- --games 500 --blue Veteran --red Veteran`.
Each side plans from its own fog-filtered view. Seeds 1..N; Veteran breaks near-ties (within 0.5 score)
with the seeded RNG so games differ.

## 2026-11-23 — rules v1, first pass (content as in the spec tables)

| Matchup (200–500 games) | Blue | Red | Draw | Blue share of decisive | Avg turns |
|---|---|---|---|---|---|
| Veteran vs Veteran (500) | 41.2% | 41.8% | 17.0% | **49.6%** | 11.6 |
| Recruit vs Veteran | 19.5% | 74.5% | 6.0% | 20.7% | 11.3 |
| Commander vs Veteran | 82.0% | 11.0% | 7.0% | 88.2% | 10.5 |
| Veteran vs Commander | 10.0% | 62.5% | 27.5% | 13.8% | 11.4 |

- Targets met: mirrored Veteran 45–55% (49.6% of decisive games), average ≥ 7 turns (11.6),
  every unit type issues non-Hold orders (Barrage 3% of artillery orders, Overwatch heavy for AT/Rifle).
- Objective hold share (500 games): A North Bridge contested 93% of turns; B held by Red 83%; C held by Blue 80%.
  Each side takes its near objective; games are decided at the bridge.
- AI time: median 0.8 ms, p95 1.1 ms, max 25 ms (Veteran); Commander max 68 ms. Budget is 150 ms.
- No content changes were needed in this pass.

### Watch list
- Armor is lost in ~96% of games: AI armor contests the bridge and AT overwatch wears it down.
  Next tuning candidates if playtests agree: AT `atkVehicle` 7 → 6, or a higher AI threat weight for Armor.
