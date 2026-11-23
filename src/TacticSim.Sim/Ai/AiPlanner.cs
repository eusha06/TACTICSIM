namespace TacticSim.Sim.Ai;

public enum Difficulty { Recruit, Veteran, Commander }

/// <summary>Small seeded RNG (mulberry32). Seeded per match, turn and side, so AI choices replay exactly.</summary>
public sealed class Rng(uint seed)
{
    private uint _state = seed;

    public uint Next()
    {
        _state += 0x6D2B79F5;
        var t = _state;
        t = (t ^ (t >> 15)) * (t | 1);
        t ^= t + (t ^ (t >> 7)) * (t | 61);
        return t ^ (t >> 14);
    }

    public int Below(int count) => (int)(Next() % (uint)count);
}

/// <summary>
/// Utility-scoring AI (spec Part E). It only ever receives a fog-filtered <see cref="MatchView"/>,
/// so it can't cheat: it knows its own units, the enemies it sees and its last-seen markers.
/// </summary>
public static class AiPlanner
{
    public static Order[] Plan(GameContent content, MatchView view, Difficulty difficulty, int seed)
    {
        var board = content.Board(view.Scenario);
        var world = Knowledge(content, board, view);
        var own = world.Units.Where(u => u.Side == view.Viewer).OrderBy(u => u.Id).ToList();
        if (view.Viewer == Side.Red && board.Map.RedScript == "hold") return own.Select(u => Order.Hold(u.Id)).ToArray();
        var rng = new Rng(unchecked((uint)seed * 2654435761u + (uint)view.Turn * 40503u + (uint)view.Viewer));
        var planner = new Planner(content, board, world, view.Viewer, difficulty, rng);
        var plan = planner.Greedy(forcedUnit: null);
        if (difficulty == Difficulty.Commander) plan = planner.Refine(plan);
        return planner.Sanitize(plan.Orders);
    }

    /// <summary>What the side knows, as a match state: own units, visible enemies, and still-plausible markers.</summary>
    public static MatchState Knowledge(GameContent content, Board board, MatchView view)
    {
        var visible = view.Visible.Select(board.FromIndex).ToHashSet();
        var state = new MatchState
        {
            Scenario = view.Scenario, RulesVersion = view.RulesVersion, Turn = view.Turn, VpBlue = view.VpBlue,
            VpRed = view.VpRed, Control = new Dictionary<string, Side?>(view.Control)
        };
        foreach (var u in view.Units)
            state.Units.Add(new UnitState { Id = u.Id, Side = u.Side, Type = u.Type, Pos = u.Pos, Hp = u.Hp });
        // A marker on a hex we can see now is stale: the enemy has moved on.
        foreach (var m in view.LastSeen.Where(m => !visible.Contains(m.Pos) && state.Units.All(u => u.Pos != m.Pos)))
            state.Units.Add(new UnitState { Id = m.UnitId, Side = view.Viewer.Enemy(), Type = m.Type, Pos = m.Pos, Hp = m.Hp });
        state.Units.Sort((a, b) => a.Id.CompareTo(b.Id));
        return state;
    }

    private sealed record Candidate(Order Order, Hex End, double Fixed, string? Objective, double ObjectiveValue,
        int? TargetId, int Damage, double FireWeight);

    private sealed record PlanResult(Order[] Orders, int[] AssignmentOrder);

    private sealed class Planner
    {
        private const double KillBonus = 6;
        private readonly GameContent _content;
        private readonly Board _board;
        private readonly MatchState _world;
        private readonly Side _side;
        private readonly Difficulty _difficulty;
        private readonly Rng _rng;
        private readonly List<UnitState> _own;
        private readonly List<UnitState> _enemies;
        private readonly HashSet<Hex> _visible;
        private readonly Dictionary<int, List<Candidate>> _candidates = [];

        public Planner(GameContent content, Board board, MatchState world, Side side, Difficulty difficulty, Rng rng)
        {
            _content = content;
            _board = board;
            _world = world;
            _side = side;
            _difficulty = difficulty;
            _rng = rng;
            _own = world.Units.Where(u => u.Side == side).ToList();
            _enemies = world.Units.Where(u => u.Side != side).ToList();
            _visible = Vision.VisibleHexes(content, board, _own);
            foreach (var unit in _own) _candidates[unit.Id] = CandidatesFor(unit);
        }

        private double ThreatWeight => _difficulty switch { Difficulty.Recruit => 0, Difficulty.Veteran => 0.7, _ => 0.9 };
        private bool Seen(UnitState enemy) => _visible.Contains(enemy.Pos);

        // ---- Coordination: assign the best-scoring unit first, reserve its hex, avoid overkill. ----
        public PlanResult Greedy(int? forcedUnit)
        {
            var assigned = new Dictionary<int, Candidate>();
            var order = new List<int>();
            var reserved = new HashSet<Hex>();
            var objectiveClaims = new Dictionary<string, int>();
            var damageClaims = new Dictionary<int, int>();
            while (assigned.Count < _own.Count)
            {
                (UnitState Unit, Candidate Choice, double Score)? best = null;
                foreach (var unit in _own.Where(u => !assigned.ContainsKey(u.Id)))
                {
                    var options = _candidates[unit.Id]
                        .Where(c => !reserved.Contains(c.End) && !_own.Any(o => o != unit && !assigned.ContainsKey(o.Id) && o.Pos == c.End))
                        .Select(c => (Candidate: c, Score: Score(c, objectiveClaims, damageClaims)))
                        .OrderByDescending(x => x.Score).ToList();
                    if (options.Count == 0) options.Add((new Candidate(Order.Hold(unit.Id), unit.Pos, 0, null, 0, null, 0, 0), 0));
                    var pick = Choose(options, forced: unit.Id == forcedUnit);
                    if (best is null || pick.Score > best.Value.Score) best = (unit, pick.Candidate, pick.Score);
                }
                var (chosenUnit, choice, _) = best!.Value;
                assigned[chosenUnit.Id] = choice;
                order.Add(chosenUnit.Id);
                reserved.Add(choice.End);
                if (choice.Objective is string o) objectiveClaims[o] = objectiveClaims.GetValueOrDefault(o) + 1;
                if (choice.TargetId is int t) damageClaims[t] = damageClaims.GetValueOrDefault(t) + choice.Damage;
            }
            return new PlanResult(assigned.OrderBy(a => a.Key).Select(a => a.Value.Order).ToArray(), order.ToArray());
        }

        private (Candidate Candidate, double Score) Choose(List<(Candidate Candidate, double Score)> options, bool forced)
        {
            if (forced && options.Count > 1) return options[1];
            if (_difficulty == Difficulty.Recruit) return options[_rng.Below(Math.Min(3, options.Count))];
            // Near-equal plans are picked by the seeded RNG so the AI isn't perfectly predictable.
            var close = options.TakeWhile(x => x.Score >= options[0].Score - 0.5).ToList();
            return close[_rng.Below(close.Count)];
        }

        private double Score(Candidate c, Dictionary<string, int> objectiveClaims, Dictionary<int, int> damageClaims)
        {
            var score = c.Fixed;
            if (c.Objective is string o) score += c.ObjectiveValue / (1 + objectiveClaims.GetValueOrDefault(o));
            if (c.TargetId is int id)
            {
                var hp = _world.Unit(id)!.Hp;
                var already = damageClaims.GetValueOrDefault(id);
                var useful = Math.Max(0, Math.Min(c.Damage, hp - already));
                var kills = already < hp && already + c.Damage >= hp;
                score += c.FireWeight * (useful + (kills ? KillBonus : 0));
            }
            return score;
        }

        // ---- Candidates: reachable hexes x order types, pruned to the best 30 per unit. ----
        private List<Candidate> CandidatesFor(UnitState unit)
        {
            var def = _content.Units[unit.Type];
            var blocked = _enemies.Select(e => e.Pos).ToHashSet();
            var list = new List<Candidate>();
            var routes = Pathfinder.Reachable(_content, _board, def, unit.Pos, blocked).Values
                .Select(p => (p.Dest, p.Path)).Prepend((unit.Pos, Array.Empty<Hex>()));
            foreach (var (dest, path) in routes)
            {
                var (objective, objectiveValue) = ObjectiveValue(unit, def, dest);
                if (path.Length == 0)
                {
                    var holdTarget = BestTarget(unit, def, dest, directOnly: false);
                    list.Add(Make(Order.Hold(unit.Id), dest, Place(unit, def, dest, holding: true),
                        objective, objectiveValue, holdTarget, 0.85));
                    var approach = _enemies.Where(e => Seen(e) && HexMath.Distance(e.Pos, dest) <= def.MaxRange + _content.Units[e.Type].Move)
                        .Select(e => (Enemy: e, Damage: Combat.Damage(_content, def, _content.Units[e.Type], 0)))
                        .OrderByDescending(x => x.Damage).ThenBy(x => x.Enemy.Id).FirstOrDefault();
                    if (approach.Enemy is not null && def.CannotMoveAndFire is false)
                        list.Add(new Candidate(new Order(unit.Id, OrderType.Overwatch), dest, Place(unit, def, dest, false),
                            objective, objectiveValue, approach.Enemy.Id, approach.Damage, 0.6));
                    if (holdTarget is { } now && Seen(now))
                        list.Add(Make(new Order(unit.Id, OrderType.Attack, TargetId: now.Id), dest, Place(unit, def, dest, false),
                            objective, objectiveValue, now, 1.0));
                    if (def.Indirect) list.AddRange(Barrages(unit, def));
                }
                else if (def.CannotMoveAndFire)
                    list.Add(new Candidate(new Order(unit.Id, OrderType.Move, path), dest, Place(unit, def, dest, false), objective, objectiveValue, null, 0, 0));
                else
                {
                    var target = BestTarget(unit, def, dest, directOnly: true);
                    var attack = target is not null && Seen(target) && objective is null;
                    var order = attack ? new Order(unit.Id, OrderType.Attack, path, target!.Id) : new Order(unit.Id, OrderType.Move, path);
                    list.Add(Make(order, dest, Place(unit, def, dest, false), objective, objectiveValue, target, attack ? 0.8 : 0.7));
                }
            }
            var claims = new Dictionary<string, int>();
            var damage = new Dictionary<int, int>();
            return list.OrderByDescending(c => Score(c, claims, damage)).ThenBy(c => _board.Index(c.End)).Take(30).ToList();
        }

        private Candidate Make(Order order, Hex end, double fixedScore, string? objective, double objectiveValue, UnitState? target, double weight)
        {
            if (target is null) return new Candidate(order, end, fixedScore, objective, objectiveValue, null, 0, 0);
            var shooter = new UnitState { Type = _own.First(u => u.Id == order.UnitId).Type, Pos = end, Hp = 1 };
            var damage = Combat.Damage(_content, _board, shooter, target, false);
            return new Candidate(order, end, fixedScore, objective, objectiveValue, target.Id, damage, Seen(target) ? weight : weight * 0.4);
        }

        private IEnumerable<Candidate> Barrages(UnitState unit, UnitDef def)
        {
            foreach (var enemy in _enemies.Where(e => Seen(e) && Combat.InRange(def, unit.Pos, e.Pos)))
            {
                if (_own.Any(o => HexMath.Distance(o.Pos, enemy.Pos) <= 1)) continue; // friendly-fire risk
                var onObjective = _board.ObjectiveAt(enemy.Pos) is not null;
                var shooter = new UnitState { Type = unit.Type, Pos = unit.Pos, Hp = 1 };
                yield return new Candidate(new Order(unit.Id, OrderType.Barrage, TargetHex: enemy.Pos), unit.Pos,
                    Place(unit, def, unit.Pos, false), null, 0, enemy.Id,
                    Combat.Damage(_content, _board, shooter, enemy, true), onObjective ? 0.9 : 0.45);
            }
        }

        private UnitState? BestTarget(UnitState unit, UnitDef def, Hex from, bool directOnly)
        {
            var shooter = new UnitState { Id = unit.Id, Side = unit.Side, Type = unit.Type, Pos = from, Hp = unit.Hp };
            var targets = _enemies.Where(e => Combat.InRange(def, from, e.Pos) &&
                (def.Indirect ? !directOnly && Seen(e) : Vision.HasLos(_board, from, e.Pos))).ToList();
            return Combat.BestTarget(_content, _board, shooter, targets, _ => false);
        }

        /// <summary>Everything about standing on a hex that doesn't depend on teammates: cover, threat, risk, spotting.</summary>
        private double Place(UnitState unit, UnitDef def, Hex dest, bool holding)
        {
            var here = new UnitState { Id = unit.Id, Side = unit.Side, Type = unit.Type, Pos = dest, Hp = unit.Hp };
            var score = 0.6 * Combat.Cover(_content, _board, here, holding);
            score -= ThreatWeight * _enemies.Sum(e => Threat(e, here, holding));
            var near = _enemies.Count(e => HexMath.Distance(e.Pos, dest) <= 2);
            if (!def.Captures) score -= 4 * near; // keep artillery and recon out of contact
            if (unit.Type == "recon")
            {
                var spotter = new[] { here };
                var sees = Vision.VisibleHexes(_content, _board, spotter);
                score += 2 * _enemies.Count(e => sees.Contains(e.Pos)) + 0.05 * sees.Count(h => !_visible.Contains(h));
            }
            if (def.Indirect)
                score += 1.5 * _board.Map.Objectives.Count(o => Combat.InRange(def, dest, o.Hex))
                       + _enemies.Count(e => Combat.InRange(def, dest, e.Pos));
            return score;
        }

        private double Threat(UnitState enemy, UnitState target, bool holding)
        {
            var e = _content.Units[enemy.Type];
            var distance = HexMath.Distance(enemy.Pos, target.Pos);
            var chance = e.Indirect ? (Combat.InRange(e, enemy.Pos, target.Pos) ? 0.6 : 0)
                : distance <= e.MaxRange ? 1.0 : distance <= e.MaxRange + e.Move / 2 ? 0.5 : 0;
            if (chance == 0) return 0;
            return chance * (Seen(enemy) ? 1 : 0.5) * Combat.Damage(_content, _board, enemy, target, holding);
        }

        /// <summary>Capturing units are pulled toward the best objective: 12 on it, minus 0.8 per tick of travel.</summary>
        private (string? Id, double Value) ObjectiveValue(UnitState unit, UnitDef def, Hex dest)
        {
            if (!def.Captures) return (null, 0);
            (string? Id, double Value) best = (null, 0);
            foreach (var o in _board.Map.Objectives)
            {
                var ticks = CostTo(def, o.Hex)[_board.Index(dest)];
                if (ticks == int.MaxValue) continue;
                var worth = 12.0 * (_world.Control[o.Id] == _side.Enemy() ? 1.3 : 1.0)
                          + (dest == o.Hex && _world.Control[o.Id] == _side ? 2 : 0);
                var value = Math.Max(0, worth - 0.8 * ticks);
                if (value > best.Value) best = (o.Id, value);
            }
            return best;
        }

        private readonly Dictionary<(string, Hex), int[]> _costs = [];
        private int[] CostTo(UnitDef def, Hex target)
        {
            if (!_costs.TryGetValue((def.Id, target), out var field))
                _costs[(def.Id, target)] = field = Pathfinder.CostTo(_board, def, target);
            return field;
        }

        // ---- Commander: re-score the top plans against three likely enemy responses. ----
        public PlanResult Refine(PlanResult first)
        {
            var plans = new List<PlanResult> { first };
            foreach (var unitId in first.AssignmentOrder.Take(4))
            {
                var alternative = Greedy(forcedUnit: unitId);
                if (plans.All(p => !p.Orders.SequenceEqual(alternative.Orders))) plans.Add(alternative);
            }
            var enemyUnits = _enemies.OrderBy(e => e.Id).ToList();
            var responses = new List<Order[]>
            {
                enemyUnits.Select(e => Order.Hold(e.Id)).ToArray(),
                enemyUnits.Select(e => new Order(e.Id, OrderType.Overwatch)).ToArray(),
            };
            if (enemyUnits.Count > 0)
            {
                var theirView = Fog.StateFor(_content, _world, _side.Enemy());
                var theirWorld = Knowledge(_content, _board, theirView);
                var theirs = new Planner(_content, _board, theirWorld, _side.Enemy(), Difficulty.Veteran, new Rng(7)).Greedy(null);
                responses.Add(theirs.Orders);
            }
            return plans.Select((plan, index) => (plan, index, score: Evaluate(plan.Orders, responses)))
                .OrderByDescending(x => x.score).ThenBy(x => x.index).First().plan;
        }

        private double Evaluate(Order[] plan, List<Order[]> responses)
        {
            var outcomes = responses.Select(response =>
            {
                var result = _side == Side.Blue
                    ? TurnResolver.Resolve(_content, _world, plan, response)
                    : TurnResolver.Resolve(_content, _world, response, plan);
                var end = result.FinalState;
                double Hp(MatchState s, Side side) => s.Units.Where(u => u.Side == side).Sum(u => u.Hp);
                int Dead(MatchState s, Side side) => s.Units.Count(u => u.Side == side && !u.Alive);
                var gained = _side == Side.Blue ? result.VpBlue - result.VpRed : result.VpRed - result.VpBlue;
                return (Hp(_world, _side.Enemy()) - Hp(end, _side.Enemy())) - (Hp(_world, _side) - Hp(end, _side))
                    + 8 * (Dead(end, _side.Enemy()) - Dead(end, _side)) + 6 * gained
                    + 3 * (end.Control.Count(c => c.Value == _side) - end.Control.Count(c => c.Value == _side.Enemy()));
            }).ToList();
            return 0.5 * outcomes.Min() + 0.5 * outcomes.Average();
        }

        /// <summary>Belt and braces: any order the rules reject becomes Hold.</summary>
        public Order[] Sanitize(Order[] orders)
        {
            var list = orders.ToList();
            while (true)
            {
                var check = OrderRules.Validate(_content, _world, _side, list);
                if (check.Ok) return check.Orders;
                var index = list.FindIndex(o => o.UnitId == check.UnitId);
                if (index < 0) return _own.Select(u => Order.Hold(u.Id)).ToArray();
                list[index] = Order.Hold(list[index].UnitId);
            }
        }
    }
}
