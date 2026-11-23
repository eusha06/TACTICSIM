import { describe, expect, it } from 'vitest';
import { OrderBook, Preview } from '../src/planner';
import { Board, type Content } from '../src/rules/content';
import type { MatchView } from '../src/types';
import rules from '../../content/v1/rules.json';
import terrain from '../../content/v1/terrain.json';
import units from '../../content/v1/units.json';
import bootcamp from '../../content/v1/maps/bootcamp.json';

const content = { rulesVersion: 'v1', rules, terrain: terrain.terrain, units: units.units, maps: [bootcamp] } as unknown as Content;
const board = new Board(content.maps[0], content);
const view = (extra: Partial<MatchView> = {}): MatchView => ({
  scenario: 'bootcamp', rulesVersion: 'v1', viewer: 'blue', turn: 2, turnLimit: 8, vpToWin: 1, vpBlue: 0, vpRed: 0,
  units: [
    { id: 1, side: 'blue', type: 'rifle', pos: { col: 1, row: 4 }, hp: 10, maxHp: 10 },
    { id: 2, side: 'blue', type: 'armor', pos: { col: 1, row: 2 }, hp: 18, maxHp: 18 },
    { id: 3, side: 'red', type: 'rifle', pos: { col: 7, row: 3 }, hp: 5, maxHp: 10 },
  ],
  lastSeen: [], control: { X: null }, visible: [...board.all()].map((h) => board.index(h)), vpHistory: [], kills: 0, losses: 0, fogLifted: false,
  ...extra,
});

describe('order book', () => {
  it('undoes and redoes order changes', () => {
    const book = new OrderBook();
    book.set({ unitId: 1, type: 'hold' });
    book.set({ unitId: 1, type: 'overwatch' });
    book.set({ unitId: 2, type: 'hold' });
    expect(book.size).toBe(2);
    expect(book.undo()).toBe(true);
    expect(book.size).toBe(1);
    expect(book.get(1)?.type).toBe('overwatch');
    expect(book.undo()).toBe(true);
    expect(book.get(1)?.type).toBe('hold');
    expect(book.redo()).toBe(true);
    expect(book.get(1)?.type).toBe('overwatch');
    book.remove(1);
    expect(book.canRedo).toBe(false);
    expect(book.all().map((o) => o.unitId)).toEqual([]);
  });
});

describe('planning preview', () => {
  it('plans an attack that drives into range when the target is too far', () => {
    const p = new Preview(board, view());
    const plan = p.attackOrder(p.own[1], p.enemies[0], new OrderBook());
    expect(plan.order?.type).toBe('attack');
    expect(plan.order?.path?.length).toBeGreaterThan(0);
    const end = plan.order!.path!.at(-1)!;
    expect(p.combat(p.own[1], p.enemies[0], end).problem).toBeNull();
  });

  it('writes the combat card in the spec format', () => {
    const p = new Preview(board, view({ units: [...view().units.slice(0, 2), { id: 3, side: 'red', type: 'rifle', pos: { col: 3, row: 2 }, hp: 10, maxHp: 10 }] }));
    expect(p.combat(p.own[1], p.enemies[0]).text).toBe('Your Armor → Enemy Rifle Infantry (Road, cover 0): 6 − 0 = 6 dmg · HP 10 → 4');
  });

  it('refuses ends that clash with another unit and explains why', () => {
    const p = new Preview(board, view());
    const book = new OrderBook();
    book.set({ unitId: 1, type: 'move', path: [{ col: 2, row: 4 }] });
    expect(p.moveProblem(p.own[1], { col: 2, row: 4 }, book)).toMatch(/already the end hex/);
    expect(p.moveProblem(p.own[1], { col: 8, row: 6 }, book)).toMatch(/Out of reach/);
  });

  it('flags barrage problems and friendly fire risk', () => {
    const p = new Preview(board, view({ units: [...view().units, { id: 4, side: 'blue', type: 'artillery', pos: { col: 0, row: 0 }, hp: 9, maxHp: 9 }] }));
    const art = p.own.find((u) => u.type === 'artillery')!;
    expect(p.barrageProblem(art, { col: 1, row: 0 })).toBe('Out of range: needs 3–6, target is 1.');
    expect(p.barrageProblem(art, { col: 4, row: 0 })).toBeNull();
    expect(p.friendlyFireRisk({ col: 1, row: 4 }, new OrderBook()).map((u) => u.id)).toEqual([1]);
  });
});
