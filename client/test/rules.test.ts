import { describe, expect, it } from 'vitest';
import { Board, type Content } from '../src/rules/content';
import { distance, line, type Hex } from '../src/rules/hex';
import { blocker, cover, damage, reachable, visibleHexes } from '../src/rules/rules';
import rules from '../../content/v1/rules.json';
import terrain from '../../content/v1/terrain.json';
import units from '../../content/v1/units.json';
import rivergate from '../../content/v1/maps/rivergate.json';
import bootcamp from '../../content/v1/maps/bootcamp.json';
// Golden vectors are written by tests/TacticSim.Sim.Tests/GoldenVectors.cs (dotnet test).
import hexVectors from './vectors/hex.json';
import losVectors from './vectors/los.json';
import pathVectors from './vectors/paths.json';
import damageVectors from './vectors/damage.json';
import turnVector from './vectors/turn.json';

/* eslint-disable @typescript-eslint/no-explicit-any */
const vectors = (name: string): any => ({ 'hex.json': hexVectors, 'los.json': losVectors, 'paths.json': pathVectors, 'damage.json': damageVectors, 'turn.json': turnVector } as any)[name];
const content = { rulesVersion: 'v1', rules, terrain: terrain.terrain, units: units.units, maps: [rivergate, bootcamp] } as unknown as Content;
const board = (id: string) => new Board(content.maps.find((m) => m.id === id)!, content);
const plain = (h: Hex) => ({ col: h.col, row: h.row });

describe('rules mirror matches the C# engine', () => {
  it('hex distance and lines', () => {
    const cases = vectors('hex.json');
    expect(cases.length).toBeGreaterThan(500);
    for (const c of cases) {
      expect(distance(c.a, c.b)).toBe(c.distance);
      expect(line(c.a, c.b)).toEqual(c.line.map(plain));
    }
  });

  it('line of sight and vision', () => {
    const { los, vision } = vectors('los.json');
    const b = board('rivergate');
    for (const c of los) expect(blocker(b, c.from, c.to)).toEqual(c.blocker ? plain(c.blocker) : null);
    for (const v of vision) expect([...visibleHexes(b, [{ type: v.unit, pos: v.at }])].sort((x, y) => x - y)).toEqual(v.visible);
  });

  it('reachable hexes, costs, paths and road march', () => {
    for (const c of vectors('paths.json')) {
      const b = board(c.map);
      const blocked = new Set<number>(c.blocked.map((h: Hex) => b.index(h)));
      const reach = reachable(b, b.unit(c.unit), c.start, blocked);
      const mine = [...reach.values()].sort((x, y) => b.index(x.dest) - b.index(y.dest));
      expect(mine.map((p) => ({ dest: p.dest, cost: p.cost, path: p.path, roadMarch: p.roadMarch }))).toEqual(
        c.reachable.map((p: { dest: Hex; cost: number; path: Hex[]; roadMarch: boolean }) => ({
          dest: plain(p.dest), cost: p.cost, path: p.path.map(plain), roadMarch: p.roadMarch,
        })),
      );
    }
  });

  it('cover and damage', () => {
    const b = board('rivergate');
    const spot = (terrain: string) => [...b.all()].find((h) => b.terrainAt(h).id === terrain)!;
    for (const c of vectors('damage.json')) {
      const coverValue = cover(b, c.target, spot(c.terrain), c.holding);
      expect(coverValue).toBe(c.cover);
      expect(damage(b, b.unit(c.attacker), b.unit(c.target), coverValue)).toBe(c.damage);
    }
  });

  it('per-tick fog in a resolved turn matches recomputed vision', () => {
    const { result } = vectors('turn.json');
    const b = board('rivergate');
    for (const tick of result.ticks) {
      for (const [side, field] of [['blue', 'blueVisible'], ['red', 'redVisible']] as const) {
        const own = tick.units.filter((u: { side: string; hp: number }) => u.side === side && u.hp > 0);
        expect([...visibleHexes(b, own)].sort((x, y) => x - y)).toEqual(tick[field]);
      }
    }
  });
});
