namespace TacticSim.Sim;

public static class EventKind
{
    public const string Move = "move";          // UnitId From -> To, Ticks = hex cost
    public const string Blocked = "blocked";    // UnitId wanted To; Reason collision | enemy | friendly
    public const string Contact = "contact";    // UnitId stopped next to TargetId
    public const string GiveWay = "giveWay";    // UnitId stepped back From -> To (friendly already there)
    public const string Spotted = "spotted";    // Side now sees TargetId at To
    public const string Lost = "lost";          // Side lost sight of TargetId, last seen at To
    public const string Shot = "shot";          // direct fire UnitId -> TargetId
    public const string Shell = "shell";        // indirect fire UnitId -> TargetId
    public const string Barrage = "barrage";    // shell lands on hex To; TargetId = unit hit (if any)
    public const string Destroyed = "destroyed";
    public const string Control = "control";    // Objective now held by Side (null = nobody)
    public const string Vp = "vp";              // Side gains Amount
    public const string Result = "result";      // match over: Side won (null = draw), Reason
}

public sealed record SimEvent(string Kind, int? UnitId = null, int? TargetId = null, Hex? From = null, Hex? To = null,
    int Damage = 0, int Hp = 0, bool Destroyed = false, Side? Side = null, string? Objective = null,
    string? Reason = null, int Ticks = 0, int Amount = 0);

public sealed record UnitFrame(int Id, Side Side, string Type, Hex Pos, int Hp);

/// <summary>One tick: unit positions after it, what happened, and what each side could see.</summary>
public sealed record TickRecord(int Tick, UnitFrame[] Units, SimEvent[] Events,
    int[] BlueVisible, int[] RedVisible, int[] BlueSees, int[] RedSees);

public sealed record TurnResult(int Turn, TickRecord[] Ticks, SimEvent[] EndEvents, MatchState FinalState, int VpBlue, int VpRed);

/// <summary>
/// Resolves one simultaneous turn (Part D of the spec). Pure: same state and orders always give
/// the same result. Every loop runs in unit-id order.
/// </summary>
public static class TurnResolver
{
    public static TurnResult Resolve(GameContent content, MatchState start, IReadOnlyList<Order> blueOrders, IReadOnlyList<Order> redOrders)
    {
        if (start.IsOver) throw new InvalidOperationException("The match is already over.");
        var board = content.Board(start.Scenario);
        var state = start.Clone();
        // Orders are validated upstream; an order for another side's unit is simply ignored here.
        var given = blueOrders.Where(o => state.Unit(o.UnitId)?.Side == Side.Blue)
            .Concat(redOrders.Where(o => state.Unit(o.UnitId)?.Side == Side.Red))
            .GroupBy(o => o.UnitId).ToDictionary(g => g.Key, g => g.First());
        var runners = state.Units.Where(u => u.Alive).OrderBy(u => u.Id)
            .Select(u => new Runner(u, content.Units[u.Type], given.GetValueOrDefault(u.Id) ?? Order.Hold(u.Id))).ToList();
        var turn = new TurnRun(content, board, state, runners);

        var ticks = new List<TickRecord>();
        for (var tick = 1; tick <= content.Rules.TicksPerTurn; tick++)
        {
            var events = new List<SimEvent>();
            var moved = turn.MoveStep(tick, events);
            turn.ContactStep(moved, events);
            turn.GiveWayStep(events);
            turn.UpdateVision(events);
            turn.FireStep(tick, volley: false, events);
            ticks.Add(turn.Record(tick, events));
        }
        var volley = new List<SimEvent>();
        turn.FireStep(content.Rules.TicksPerTurn + 1, volley: true, volley);
        ticks.Add(turn.Record(content.Rules.TicksPerTurn + 1, volley));

        var (endEvents, vpBlue, vpRed) = turn.EndOfTurn();
        return new TurnResult(start.Turn, ticks.ToArray(), endEvents.ToArray(), state, vpBlue, vpRed);
    }

    private sealed class Runner(UnitState unit, UnitDef def, Order order)
    {
        public UnitState Unit { get; } = unit;
        public UnitDef Def { get; } = def;
        public Order Order { get; } = order;
        public Queue<Hex> Path { get; } = new(order.Moves ? order.Path! : []);
        public List<Hex> Trail { get; } = [unit.Pos];
        public int Progress { get; set; }
        public int ArrivedTick { get; set; }
        public int? DiedTick { get; set; }
        public bool Fired { get; set; }
        public int Id => Unit.Id;
        public bool Holding => Order.Type == OrderType.Hold;
        public bool CanFire => !(Def.CannotMoveAndFire && Order.Moves);

        public void Stop()
        {
            Path.Clear();
            Progress = 0;
        }
    }

    private sealed record ShotPlan(Runner Shooter, Runner? Target, Hex? Hex, string Trigger);

    /// <summary>Mutable bookkeeping for one turn.</summary>
    private sealed class TurnRun
    {
        private readonly GameContent _content;
        private readonly Board _board;
        private readonly MatchState _state;
        private readonly List<Runner> _runners;
        private readonly Dictionary<Side, SideView> _views = [];
        private readonly Dictionary<Side, HashSet<int>> _sees = [];
        private readonly Dictionary<Side, Dictionary<int, LastSeenMarker>> _memory = [];

        public TurnRun(GameContent content, Board board, MatchState state, List<Runner> runners)
        {
            _content = content;
            _board = board;
            _state = state;
            _runners = runners;
            foreach (var side in Sides)
            {
                _memory[side] = state.LastSeen(side).ToDictionary(m => m.UnitId);
                _views[side] = Vision.ViewOf(content, board, Alive.Select(r => r.Unit), side);
                _sees[side] = _views[side].VisibleEnemies.Select(e => e.Id).ToHashSet();
                Remember(side, _views[side].VisibleEnemies);
            }
        }

        private static readonly Side[] Sides = [Side.Blue, Side.Red];
        private IEnumerable<Runner> Alive => _runners.Where(r => r.Unit.Alive);

        // D2.1 + D2.2: movement and collisions. Occupancy is judged from where units stood at the start of the tick.
        public List<Runner> MoveStep(int tick, List<SimEvent> events)
        {
            var standing = Alive.ToLookup(r => r.Unit.Pos);
            var intents = new List<(Runner Runner, Hex To, int Cost)>();
            foreach (var r in Alive.Where(r => r.Path.Count > 0))
            {
                var next = r.Path.Peek();
                var cost = _board.MoveCost(next, r.Def) ?? int.MaxValue;
                r.Progress++;
                // A cost-2 hex takes two ticks: in transit on the first, arriving on the second.
                if (r.Progress >= cost) intents.Add((r, next, cost));
            }
            var crowded = intents.GroupBy(i => i.To).Where(g => g.Count() > 1)
                .SelectMany(g => g.Select(i => i.Runner.Id)).ToHashSet();
            var moved = new List<Runner>();
            foreach (var (r, to, cost) in intents)
            {
                var from = r.Unit.Pos;
                var occupants = standing[to].Where(o => o != r).ToList();
                var enemy = occupants.FirstOrDefault(o => o.Unit.Side != r.Unit.Side);
                if (crowded.Contains(r.Id))
                {
                    r.Stop();
                    events.Add(new(EventKind.Blocked, r.Id, From: from, To: to, Reason: "collision"));
                }
                else if (enemy is not null)
                {
                    // Bumping into a hidden enemy stops you; being adjacent, it is now in sight.
                    r.Stop();
                    events.Add(new(EventKind.Blocked, r.Id, enemy.Id, from, to, Reason: "enemy"));
                }
                else if (occupants.Count > 0 && r.Path.Count == 1)
                {
                    // Friendlies pass through each other but never finish on the same hex.
                    r.Stop();
                    events.Add(new(EventKind.Blocked, r.Id, occupants[0].Id, from, to, Reason: "friendly"));
                }
                else
                {
                    r.Unit.Pos = to;
                    r.Path.Dequeue();
                    r.Progress = 0;
                    r.Trail.Add(to);
                    r.ArrivedTick = tick;
                    moved.Add(r);
                    events.Add(new(EventKind.Move, r.Id, From: from, To: to, Ticks: cost));
                }
            }
            return moved;
        }

        // D2.3: a unit that enters a hex next to an enemy it can see stops there.
        public void ContactStep(List<Runner> moved, List<SimEvent> events)
        {
            foreach (var r in moved.Where(r => r.Path.Count > 0))
            {
                var enemy = Alive.FirstOrDefault(e => e.Unit.Side != r.Unit.Side && HexMath.Adjacent(e.Unit.Pos, r.Unit.Pos)
                    && Vision.CanSee(_content, _board, r.Unit, e.Unit.Pos));
                if (enemy is null) continue;
                r.Stop();
                events.Add(new(EventKind.Contact, r.Id, enemy.Id, To: r.Unit.Pos));
            }
        }

        // A unit that stopped on a hex a friendly already holds steps back to the last free hex on its route.
        public void GiveWayStep(List<SimEvent> events)
        {
            foreach (var r in Alive.Where(r => r.Path.Count == 0).ToList())
            {
                if (!Alive.Any(o => o != r && o.Unit.Pos == r.Unit.Pos && o.ArrivedTick < r.ArrivedTick)) continue;
                for (var k = r.Trail.Count - 2; k >= 0; k--)
                {
                    var hex = r.Trail[k];
                    if (Alive.Any(o => o.Unit.Pos == hex)) continue;
                    events.Add(new(EventKind.GiveWay, r.Id, From: r.Unit.Pos, To: hex));
                    r.Unit.Pos = hex;
                    break;
                }
                // ponytail: if every hex on the route is taken the unit shares the hex; only reachable
                // with 3+ friendlies crossing one route, and sharing harms nothing but the picture.
            }
        }

        // D2.4: vision for both sides; spotted / lost events.
        public void UpdateVision(List<SimEvent> events)
        {
            foreach (var side in Sides)
            {
                var view = Vision.ViewOf(_content, _board, Alive.Select(r => r.Unit), side);
                var now = view.VisibleEnemies.Select(e => e.Id).ToHashSet();
                foreach (var e in view.VisibleEnemies.Where(e => !_sees[side].Contains(e.Id)))
                    events.Add(new(EventKind.Spotted, TargetId: e.Id, To: e.Pos, Side: side));
                foreach (var id in _sees[side].Order().Where(id => !now.Contains(id) && _state.Unit(id)!.Alive))
                    events.Add(new(EventKind.Lost, TargetId: id, To: _memory[side][id].Pos, Side: side));
                _views[side] = view;
                _sees[side] = now;
                Remember(side, view.VisibleEnemies);
            }
        }

        // D2.5 / D2.6: collect every valid shot, then apply all damage at once.
        public void FireStep(int tick, bool volley, List<SimEvent> events)
        {
            var plans = new List<ShotPlan>();
            foreach (var r in Alive.Where(r => !r.Fired && r.CanFire))
            {
                var visible = _views[r.Unit.Side].VisibleHexes;
                var fireable = Alive.Where(e => e.Unit.Side != r.Unit.Side
                    && Combat.CanFireAt(_content, _board, r.Unit, e.Unit, visible)).ToList();
                if (volley)
                {
                    if (r.Order.Type is not (OrderType.Hold or OrderType.Move)) continue;
                    var target = fireable.FirstOrDefault(e => e.Id == r.Order.TargetId) ?? Best(r, fireable);
                    if (target is not null) plans.Add(new(r, target, null, "fireAtWill"));
                }
                else if (r.Order.Type == OrderType.Overwatch && Best(r, fireable) is { } first)
                    plans.Add(new(r, first, null, "overwatch"));
                else if (r.Order.Type == OrderType.Attack && fireable.FirstOrDefault(e => e.Id == r.Order.TargetId) is { } mark)
                {
                    plans.Add(new(r, mark, null, "attack"));
                    r.Stop(); // closed to range: stop and fire
                }
                else if (r.Order.Type == OrderType.Barrage && tick == _content.Rules.BarrageTick)
                    plans.Add(new(r, null, r.Order.TargetHex, "barrage"));
            }

            // Damage is worked out from the state before anyone is hit, so mutual kills happen.
            var hits = new List<(ShotPlan Plan, Runner? Target, int Damage)>();
            foreach (var plan in plans)
            {
                var targets = plan.Hex is Hex hex ? Alive.Where(v => v.Unit.Pos == hex).ToList() : [plan.Target!];
                if (targets.Count == 0) hits.Add((plan, null, 0));
                foreach (var t in targets) hits.Add((plan, t, Combat.Damage(_content, _board, plan.Shooter.Unit, t.Unit, t.Holding)));
            }
            var killed = new List<Runner>();
            foreach (var (plan, target, damage) in hits)
            {
                var shooter = plan.Shooter;
                shooter.Fired = true;
                if (target is null)
                {
                    events.Add(new(EventKind.Barrage, shooter.Id, From: shooter.Unit.Pos, To: plan.Hex, Reason: plan.Trigger));
                    continue;
                }
                var before = target.Unit.Hp;
                target.Unit.Hp = Math.Max(0, before - damage);
                var destroyed = before > 0 && target.Unit.Hp == 0;
                var kind = plan.Hex is not null ? EventKind.Barrage : shooter.Def.Indirect ? EventKind.Shell : EventKind.Shot;
                events.Add(new(kind, shooter.Id, target.Id, shooter.Unit.Pos, target.Unit.Pos, damage, target.Unit.Hp,
                    destroyed, Reason: plan.Trigger));
                if (!destroyed) continue;
                killed.Add(target);
                if (target.Unit.Side != shooter.Unit.Side && _sees[shooter.Unit.Side].Contains(target.Id))
                    AddKill(shooter.Unit.Side);
            }
            foreach (var dead in killed)
            {
                dead.Stop();
                dead.DiedTick = tick;
                events.Add(new(EventKind.Destroyed, TargetId: dead.Id, To: dead.Unit.Pos));
            }
            // Keep each side's memory honest: it knows the new HP of what it sees, and which seen units died.
            foreach (var side in Sides)
                foreach (var (_, target, _) in hits.Where(h => h.Target is not null && _sees[side].Contains(h.Target.Id)))
                {
                    if (target!.Unit.Alive) Remember(side, [target.Unit]);
                    else _memory[side].Remove(target.Id);
                }
        }

        public TickRecord Record(int tick, List<SimEvent> events)
        {
            var frames = _runners.Where(r => r.Unit.Alive || r.DiedTick == tick)
                .Select(r => new UnitFrame(r.Id, r.Unit.Side, r.Unit.Type, r.Unit.Pos, r.Unit.Hp)).ToArray();
            int[] Hexes(Side side) => _views[side].VisibleHexes.Select(_board.Index).Order().ToArray();
            return new TickRecord(tick, frames, events.ToArray(), Hexes(Side.Blue), Hexes(Side.Red),
                _sees[Side.Blue].Order().ToArray(), _sees[Side.Red].Order().ToArray());
        }

        // D2.7: objectives, VP, last-seen markers and the win check.
        public (List<SimEvent> Events, int VpBlue, int VpRed) EndOfTurn()
        {
            var events = new List<SimEvent>();
            var alive = _state.Units.Where(u => u.Alive).ToList();
            var capturers = alive.Where(u => _content.Units[u.Type].Captures).ToList();
            var gained = new Dictionary<Side, int> { [Side.Blue] = 0, [Side.Red] = 0 };
            foreach (var objective in _board.Map.Objectives)
            {
                bool On(Side s) => capturers.Any(u => u.Side == s && u.Pos == objective.Hex);
                bool Near(Side s) => capturers.Any(u => u.Side == s && HexMath.Distance(u.Pos, objective.Hex) <= 1);
                Side? holder = On(Side.Blue) && !Near(Side.Red) ? Side.Blue : On(Side.Red) && !Near(Side.Blue) ? Side.Red : null;
                if (holder != _state.Control[objective.Id])
                {
                    var reason = holder is not null ? "captured" : Near(Side.Blue) && Near(Side.Red) ? "contested" : "lost";
                    events.Add(new(EventKind.Control, Side: holder, Objective: objective.Id, Reason: reason));
                }
                _state.Control[objective.Id] = holder;
                if (holder is Side s) gained[s]++;
            }
            foreach (var side in Sides.Where(s => gained[s] > 0))
                events.Add(new(EventKind.Vp, Side: side, Amount: gained[side]));
            _state.VpBlue += gained[Side.Blue];
            _state.VpRed += gained[Side.Red];
            _state.VpHistory.Add(new VpPoint(_state.Turn, _state.VpBlue, _state.VpRed));

            foreach (var side in Sides)
            {
                var view = Vision.ViewOf(_content, _board, alive, side);
                Remember(side, view.VisibleEnemies);
                var seen = view.VisibleEnemies.Select(e => e.Id).ToHashSet();
                var list = _state.LastSeen(side);
                list.Clear();
                list.AddRange(_memory[side].Values.Where(m => !seen.Contains(m.UnitId)).OrderBy(m => m.UnitId));
            }

            var map = _board.Map;
            bool blueAlive = alive.Any(u => u.Side == Side.Blue), redAlive = alive.Any(u => u.Side == Side.Red);
            if (!blueAlive || !redAlive) Finish(!blueAlive && !redAlive ? null : blueAlive ? Side.Blue : Side.Red, "annihilation");
            else if (_state.VpBlue >= map.VpToWin || _state.VpRed >= map.VpToWin) Finish(Leader(), "vp");
            else if (_state.Turn >= map.TurnLimit) Finish(Leader(), "turnLimit");
            if (_state.IsOver) events.Add(new(EventKind.Result, Side: Winner(), Reason: _state.EndReason));
            else _state.Turn++;
            return (events, gained[Side.Blue], gained[Side.Red]);
        }

        private Side? Leader() => _state.VpBlue > _state.VpRed ? Side.Blue : _state.VpRed > _state.VpBlue ? Side.Red : null;
        private Side? Winner() => _state.Result switch { MatchResult.BlueWins => Side.Blue, MatchResult.RedWins => Side.Red, _ => null };

        private void Finish(Side? winner, string reason)
        {
            _state.Result = winner switch { Side.Blue => MatchResult.BlueWins, Side.Red => MatchResult.RedWins, _ => MatchResult.Draw };
            _state.EndReason = reason;
        }

        private void AddKill(Side side)
        {
            if (side == Side.Blue) _state.BlueKills++;
            else _state.RedKills++;
        }

        private Runner? Best(Runner shooter, IEnumerable<Runner> targets)
        {
            var list = targets.ToList();
            var unit = Combat.BestTarget(_content, _board, shooter.Unit, list.Select(t => t.Unit), u => list.First(t => t.Unit == u).Holding);
            return unit is null ? null : list.First(t => t.Unit == unit);
        }

        private void Remember(Side side, IEnumerable<UnitState> seen)
        {
            foreach (var e in seen) _memory[side][e.Id] = new LastSeenMarker(e.Id, e.Type, e.Pos, e.Hp, _state.Turn);
        }
    }
}
