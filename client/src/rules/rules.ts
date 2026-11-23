// LOS, vision, pathfinding and damage mirrored from TacticSim.Sim. Preview only.
import { Board, type UnitDef } from './content';
import { adjacent, distance, line, neighbors, type Hex, label } from './hex';

/** First hex strictly between that blocks sight, or null. See Vision.Blocker in C#. */
export function blocker(board: Board, from: Hex, to: Hex): Hex | null {
  if (board.terrainAt(from).hill || board.terrainAt(to).hill) return null;
  const hexes = line(from, to);
  for (let i = 1; i < hexes.length - 1; i++) {
    if (board.inBounds(hexes[i]) && board.terrainAt(hexes[i]).blocksLos) return hexes[i];
  }
  return null;
}

export const hasLos = (board: Board, from: Hex, to: Hex): boolean => blocker(board, from, to) === null;

export const visionRadius = (board: Board, type: string, at: Hex): number =>
  board.unit(type).vision + (board.terrainAt(at).hill ? board.content.rules.hillVisionBonus : 0);

export function visibleHexes(board: Board, units: ReadonlyArray<{ type: string; pos: Hex }>): Set<number> {
  const seen = new Set<number>();
  for (const u of units) {
    const radius = visionRadius(board, u.type, u.pos);
    for (const h of board.all()) {
      const i = board.index(h);
      if (!seen.has(i) && distance(u.pos, h) <= radius && hasLos(board, u.pos, h)) seen.add(i);
    }
  }
  return seen;
}

export interface PathInfo {
  dest: Hex;
  cost: number;
  path: Hex[];
  roadMarch: boolean;
}

export const budget = (board: Board, unit: UnitDef, roadMarch: boolean): number =>
  Math.min(board.content.rules.ticksPerTurn, unit.move + (roadMarch ? board.content.rules.roadMarchBonus : 0));

/** Dijkstra with tick costs; ties break by hex index exactly like Pathfinder.Reachable. */
export function reachable(board: Board, unit: UnitDef, start: Hex, blocked: Set<number>): Map<number, PathInfo> {
  const result = new Map<number, PathInfo>();
  for (const p of search(board, unit, start, blocked, budget(board, unit, false), false)) result.set(board.index(p.dest), p);
  if (unit.class === 'vehicle' && board.terrainAt(start).road)
    for (const p of search(board, unit, start, blocked, budget(board, unit, true), true))
      if (!result.has(board.index(p.dest))) result.set(board.index(p.dest), { ...p, roadMarch: true });
  return result;
}

function search(board: Board, unit: UnitDef, start: Hex, blocked: Set<number>, limit: number, roadOnly: boolean): PathInfo[] {
  const count = board.width * board.height;
  const dist = new Array<number>(count).fill(Infinity);
  const prev = new Array<number>(count).fill(-1);
  const done = new Array<boolean>(count).fill(false);
  const startIndex = board.index(start);
  dist[startIndex] = 0;
  for (;;) {
    let best = -1;
    for (let i = 0; i < count; i++) if (!done[i] && dist[i] !== Infinity && (best < 0 || dist[i] < dist[best])) best = i;
    if (best < 0) break;
    done[best] = true;
    for (const next of neighbors(board.fromIndex(best))) {
      if (!board.inBounds(next) || blocked.has(board.index(next))) continue;
      if (roadOnly && !board.terrainAt(next).road) continue;
      const cost = board.moveCost(next, unit);
      if (cost === null) continue;
      const total = dist[best] + cost;
      const index = board.index(next);
      if (total <= limit && total < dist[index]) {
        dist[index] = total;
        prev[index] = best;
      }
    }
  }
  const paths: PathInfo[] = [];
  for (let i = 0; i < count; i++) {
    if (dist[i] === Infinity || i === startIndex) continue;
    const path: Hex[] = [];
    for (let at = i; at !== startIndex; at = prev[at]) path.push(board.fromIndex(at));
    path.reverse();
    paths.push({ dest: board.fromIndex(i), cost: dist[i], path, roadMarch: false });
  }
  return paths;
}

/** Cover where the target stands: terrain + unit modifier + Hold, never below 0. */
export function cover(board: Board, targetType: string, at: Hex, holding: boolean): number {
  const t = board.terrainAt(at);
  const mod = board.unit(targetType).coverMods?.[t.id] ?? 0;
  return Math.max(0, t.cover + mod + (holding ? board.content.rules.holdCover : 0));
}

export const attackVs = (attacker: UnitDef, target: UnitDef): number =>
  target.class === 'foot' ? attacker.atkFoot : attacker.atkVehicle;

export const damage = (board: Board, attacker: UnitDef, target: UnitDef, coverValue: number): number =>
  Math.max(board.content.rules.minDamage, attackVs(attacker, target) - coverValue);

export function inRange(unit: UnitDef, from: Hex, to: Hex): boolean {
  const d = distance(from, to);
  return d >= unit.minRange && d <= unit.maxRange;
}

export const rangeProblem = (unit: UnitDef, from: Hex, to: Hex): string =>
  `Out of range: needs ${unit.minRange}–${unit.maxRange}, target is ${distance(from, to)}.`;

/** "No line of sight: Forest at E5 blocks it." or null. */
export function losProblem(board: Board, from: Hex, to: Hex): string | null {
  const wall = blocker(board, from, to);
  return wall ? `No line of sight: ${board.terrainAt(wall).name} at ${label(wall)} blocks it.` : null;
}

export { adjacent };
