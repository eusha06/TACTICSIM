// Planning helpers: the order book (with undo/redo) and preview checks for the HUD.
// Preview only — the server re-validates every order and its words are what the player sees on rejection.
import { Board, type UnitDef } from './rules/content';
import { adjacent, distance, label, same, type Hex } from './rules/hex';
import { blocker, cover, damage, inRange, losProblem, rangeProblem, reachable, type PathInfo } from './rules/rules';
import type { MatchView, Order, UnitView } from './types';

export class OrderBook {
  private orders = new Map<number, Order>();
  private undoStack: Array<Map<number, Order>> = [];
  private redoStack: Array<Map<number, Order>> = [];

  get(unitId: number): Order | undefined {
    return this.orders.get(unitId);
  }
  all(): Order[] {
    return [...this.orders.values()].sort((a, b) => a.unitId - b.unitId);
  }
  get size(): number {
    return this.orders.size;
  }
  set(order: Order) {
    this.remember();
    this.orders.set(order.unitId, order);
  }
  remove(unitId: number) {
    if (!this.orders.has(unitId)) return;
    this.remember();
    this.orders.delete(unitId);
  }
  undo(): boolean {
    const previous = this.undoStack.pop();
    if (!previous) return false;
    this.redoStack.push(new Map(this.orders));
    this.orders = previous;
    return true;
  }
  redo(): boolean {
    const next = this.redoStack.pop();
    if (!next) return false;
    this.undoStack.push(new Map(this.orders));
    this.orders = next;
    return true;
  }
  get canUndo() { return this.undoStack.length > 0; }
  get canRedo() { return this.redoStack.length > 0; }
  clear() {
    this.orders.clear();
    this.undoStack = [];
    this.redoStack = [];
  }
  private remember() {
    this.undoStack.push(new Map(this.orders));
    this.redoStack = [];
  }
}

export interface CombatPreview {
  text: string;          // "Your Armor → Enemy Rifle Infantry (Forest, cover 2): 6 − 2 = 4 dmg · HP 10 → 6"
  damage: number;
  hpAfter: number;
  from: Hex;
  losBlocker: Hex | null;
  problem: string | null; // why it can't fire from there, or null
}

/** Everything the planning HUD asks about one turn's state, computed from the fog-filtered view. */
export class Preview {
  readonly own: UnitView[];
  readonly enemies: UnitView[];
  readonly visible: Set<number>;
  private readonly reachCache = new Map<number, Map<number, PathInfo>>();

  constructor(readonly board: Board, readonly view: MatchView) {
    this.own = view.units.filter((u) => u.side === view.viewer);
    this.enemies = view.units.filter((u) => u.side !== view.viewer);
    this.visible = new Set(view.visible);
  }

  def(type: string): UnitDef {
    return this.board.unit(type);
  }

  /** Known enemy positions: visible units plus last-seen markers on hexes we can't see now. */
  knownEnemies(): Array<{ type: string; pos: Hex; hp: number; id: number; seen: boolean }> {
    return [
      ...this.enemies.map((e) => ({ ...e, seen: true })),
      ...this.view.lastSeen.filter((m) => !this.visible.has(this.board.index(m.pos))).map((m) => ({ id: m.unitId, type: m.type, pos: m.pos, hp: m.hp, seen: false })),
    ];
  }

  reach(unit: UnitView): Map<number, PathInfo> {
    let cached = this.reachCache.get(unit.id);
    if (!cached) {
      const blocked = new Set(this.enemies.map((e) => this.board.index(e.pos)));
      cached = reachable(this.board, this.def(unit.type), unit.pos, blocked);
      this.reachCache.set(unit.id, cached);
    }
    return cached;
  }

  /** Where each unit plans to finish, given the current orders. */
  ends(book: OrderBook): Map<number, Hex> {
    const ends = new Map<number, Hex>();
    for (const u of this.own) {
      const o = book.get(u.id);
      ends.set(u.id, o?.path?.length ? o.path[o.path.length - 1] : u.pos);
    }
    return ends;
  }

  /** Plain-English reason a move there is refused before even asking the server, or null. */
  moveProblem(unit: UnitView, dest: Hex, book: OrderBook): string | null {
    if (same(unit.pos, dest)) return null;
    const route = this.reach(unit).get(this.board.index(dest));
    if (!route) {
      if (this.board.moveCost(dest, this.def(unit.type)) === null) return `${this.board.terrainAt(dest).name} at ${label(dest)} is impassable.`;
      if (this.enemies.some((e) => same(e.pos, dest))) return `${label(dest)} is held by an enemy unit.`;
      return `Out of reach this turn: ${this.def(unit.type).name} has ${this.def(unit.type).move} ticks of move.`;
    }
    for (const [otherId, end] of this.ends(book))
      if (otherId !== unit.id && same(end, dest)) {
        const other = this.own.find((u) => u.id === otherId)!;
        return `${label(dest)} is already the end hex for your ${this.def(other.type).name}.`;
      }
    return null;
  }

  contactLikely(dest: Hex): boolean {
    return this.knownEnemies().some((e) => adjacent(e.pos, dest));
  }

  /** The exact damage line for the combat card, from a given firing hex. */
  combat(attacker: UnitView, target: UnitView, from: Hex = attacker.pos): CombatPreview {
    const a = this.def(attacker.type), t = this.def(target.type);
    const terrain = this.board.terrainAt(target.pos);
    const coverValue = cover(this.board, target.type, target.pos, false);
    const dmg = damage(this.board, a, t, coverValue);
    const hpAfter = Math.max(0, target.hp - dmg);
    const attack = t.class === 'foot' ? a.atkFoot : a.atkVehicle;
    const losBlocker = a.indirect ? null : blocker(this.board, from, target.pos);
    let problem: string | null = null;
    if (!inRange(a, from, target.pos)) problem = rangeProblem(a, from, target.pos);
    else if (losBlocker) problem = losProblem(this.board, from, target.pos);
    const floor = attack - coverValue < dmg ? ' (minimum)' : '';
    return {
      text: `Your ${a.name} → Enemy ${t.name} (${terrain.name}, cover ${coverValue}): ${attack} − ${coverValue} = ${dmg} dmg${floor} · HP ${target.hp} → ${hpAfter}`,
      damage: dmg, hpAfter, from, losBlocker, problem,
    };
  }

  /**
   * Turns "attack that enemy" into an order: fire from here if possible, otherwise drive to the
   * cheapest reachable hex with range and sight. Returns the order or a reason it can't work.
   */
  attackOrder(attacker: UnitView, target: UnitView, book: OrderBook): { order?: Order; problem?: string } {
    const a = this.def(attacker.type);
    const here = this.combat(attacker, target);
    if (!here.problem) return { order: { unitId: attacker.id, type: 'attack', targetId: target.id } };
    if (a.cannotMoveAndFire) return { problem: `${here.problem} ${a.name} can't move and fire in the same turn.` };
    const ends = this.ends(book);
    const options = [...this.reach(attacker).values()]
      .filter((p) => inRange(a, p.dest, target.pos) && (a.indirect || !blocker(this.board, p.dest, target.pos)))
      .filter((p) => ![...ends].some(([id, end]) => id !== attacker.id && same(end, p.dest)))
      .sort((x, y) => x.cost - y.cost || this.board.index(x.dest) - this.board.index(y.dest));
    if (options.length === 0) return { problem: `Can't get in range this turn: needs ${a.minRange}–${a.maxRange} with a clear line of sight.` };
    return { order: { unitId: attacker.id, type: 'attack', targetId: target.id, path: options[0].path } };
  }

  barrageProblem(artillery: UnitView, hex: Hex): string | null {
    const a = this.def(artillery.type);
    if (!a.indirect) return 'Only Artillery can Barrage.';
    if (!inRange(a, artillery.pos, hex)) return rangeProblem(a, artillery.pos, hex);
    if (!this.visible.has(this.board.index(hex))) return `Barrage needs a hex your units can see. ${label(hex)} is in the fog.`;
    return null;
  }

  /** Friendly units that a barrage on this hex could hit at tick 6 (warned in the preview). */
  friendlyFireRisk(hex: Hex, book: OrderBook): UnitView[] {
    const ends = this.ends(book);
    return this.own.filter((u) => {
      const o = book.get(u.id);
      return same(ends.get(u.id), hex) || (o?.path ?? []).some((h) => same(h, hex)) || same(u.pos, hex);
    });
  }

  /** Hexes known enemies could hit next turn (threat overlay). Ignores sight lines: better safe. */
  threat(): Set<number> {
    const hexes = new Set<number>();
    const ownHexes = new Set(this.own.map((u) => this.board.index(u.pos)));
    for (const e of this.knownEnemies()) {
      const d = this.def(e.type);
      const firing = d.cannotMoveAndFire ? [e.pos] : [e.pos, ...[...reachable(this.board, d, e.pos, ownHexes).values()].map((p) => p.dest)];
      for (const h of this.board.all())
        if (firing.some((f) => inRange(d, f, h))) hexes.add(this.board.index(h));
    }
    return hexes;
  }
}

export const describeOrder = (order: Order | undefined, board: Board, units: UnitView[]): string => {
  if (!order) return 'No order — will Hold';
  const target = units.find((u) => u.id === order.targetId);
  const end = order.path?.length ? label(order.path[order.path.length - 1]) : null;
  switch (order.type) {
    case 'move': return `Move to ${end}${target ? `, then fire at ${board.unit(target.type).name}` : ''}`;
    case 'attack': return `Attack ${target ? board.unit(target.type).name : 'target'}${end ? ` via ${end}` : ''}`;
    case 'hold': return 'Hold: +1 cover, fire at will';
    case 'overwatch': return 'Overwatch: fire at first enemy in range';
    case 'barrage': return `Barrage ${order.targetHex ? label(order.targetHex) : ''} at tick ${board.content.rules.barrageTick}`;
  }
};

export { distance };
