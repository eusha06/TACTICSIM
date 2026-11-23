import Phaser from 'phaser';
import { HEX_R, HEX_W, ART_SCALE } from './art';
import { Board } from './rules/content';
import { neighbors, type Hex, label } from './rules/hex';
import type { Side } from './types';

export const MARGIN = 80;

export function center(h: Hex): { x: number; y: number } {
  return { x: MARGIN + HEX_W * (h.col + 0.5 * (h.row & 1)) + HEX_W / 2, y: MARGIN + HEX_R + h.row * 1.5 * HEX_R };
}

export function worldSize(board: Board) {
  return { width: 2 * MARGIN + HEX_W * (board.width + 0.5), height: 2 * MARGIN + HEX_R * (1.5 * (board.height - 1) + 2) };
}

/** The hex under a world point, or null off the map. */
export function hexAt(board: Board, x: number, y: number): Hex | null {
  const row = Math.round((y - MARGIN - HEX_R) / (1.5 * HEX_R));
  let best: Hex | null = null;
  let bestDistance = Infinity;
  for (let r = row - 1; r <= row + 1; r++) {
    const col = Math.round((x - MARGIN - HEX_W / 2 - 0.5 * HEX_W * (r & 1)) / HEX_W);
    for (let c = col - 1; c <= col + 1; c++) {
      const h = { col: c, row: r };
      if (!board.inBounds(h)) continue;
      const p = center(h);
      const d = (p.x - x) ** 2 + (p.y - y) ** 2;
      if (d < bestDistance) { bestDistance = d; best = h; }
    }
  }
  return best && bestDistance <= HEX_R * HEX_R ? best : null;
}

export function corners(h: Hex, r = HEX_R): Phaser.Math.Vector2[] {
  const c = center(h);
  return Array.from({ length: 6 }, (_, i) => {
    const a = Phaser.Math.DegToRad(60 * i - 90);
    return new Phaser.Math.Vector2(c.x + r * Math.cos(a), c.y + r * Math.sin(a));
  });
}

// Neighbor direction (E, NE, NW, W, SW, SE) -> the two corners of the shared edge.
const EDGE: Array<[number, number]> = [[1, 2], [0, 1], [5, 0], [4, 5], [3, 4], [2, 3]];

export const DEPTH = { table: 0, tile: 1, deco: 2, decal: 3, fog: 4, overlay: 5, flag: 6, ghost: 7, unit: 8, fx: 9, top: 10 };

/** Draws the static diorama once and keeps the handles that change: fog, flags, trees. */
export class MapView {
  readonly fog = new Map<number, Phaser.GameObjects.Image>();
  readonly flags = new Map<string, Phaser.GameObjects.Image>();
  private readonly flagSide = new Map<string, Side | null>();

  constructor(private readonly scene: Phaser.Scene, readonly board: Board, reducedMotion: boolean) {
    const { width, height } = worldSize(board);
    const table = scene.add.graphics().setDepth(DEPTH.table);
    table.fillStyle(0x17201b, 1).fillRect(-2000, -2000, width + 4000, height + 4000);
    table.fillStyle(0x0c120f, 0.55).fillRoundedRect(18, 26, width - 24, height - 22, 28);
    table.fillStyle(0x3a2c20, 1).fillRoundedRect(12, 12, width - 24, height - 24, 26);
    table.fillStyle(0x4b3a2a, 1).fillRoundedRect(20, 20, width - 40, height - 40, 20);
    table.fillStyle(0x26321f, 1).fillRoundedRect(34, 34, width - 68, height - 68, 14);

    const variant = (h: Hex) => (h.col * 7 + h.row * 13 + ((h.col * h.row) % 5)) % 3;
    for (const h of board.all()) {
      const t = board.terrainAt(h);
      const c = center(h);
      scene.add.image(c.x, c.y, `t-${t.id}-${variant(h)}`).setScale(1 / ART_SCALE).setDepth(DEPTH.tile);
    }
    this.drawRoadsAndBanks();
    this.drawTrees(reducedMotion);
    if (!reducedMotion) this.shimmer();
    for (const o of board.map.objectives) this.drawObjective(o.id, { col: o.col, row: o.row });
    for (const h of board.all()) {
      const c = center(h);
      this.fog.set(board.index(h), scene.add.image(c.x, c.y, 'fog').setScale(1 / ART_SCALE).setDepth(DEPTH.fog).setAlpha(0.78));
    }
  }

  private isRoad = (h: Hex) => this.board.inBounds(h) && !!this.board.terrainAt(h).road;

  private drawRoadsAndBanks() {
    const g = this.scene.add.graphics().setDepth(DEPTH.deco);
    const segments: Array<[number, number, number, number]> = [];
    for (const h of this.board.all()) {
      if (!this.isRoad(h)) continue;
      const a = center(h);
      const near = neighbors(h);
      let links = 0;
      near.forEach((n) => {
        if (!this.isRoad(n)) return;
        const b = center(n);
        segments.push([a.x, a.y, (a.x + b.x) / 2, (a.y + b.y) / 2]);
        links++;
      });
      // Roads run off the table at the map edge.
      if (links === 1 && (h.col === 0 || h.col === this.board.width - 1)) {
        const out = near[h.col === 0 ? 3 : 0];
        const b = center(out);
        segments.push([a.x, a.y, (a.x + b.x) / 2, (a.y + b.y) / 2]);
      }
    }
    for (const [width, color, alpha] of [[16, 0x4a3a26, 0.6], [12, 0xa08a5f, 1], [4, 0xb9a273, 0.7]] as const) {
      g.lineStyle(width, color, alpha);
      for (const [x1, y1, x2, y2] of segments) g.lineBetween(x1, y1, x2, y2);
      for (const h of this.board.all()) if (this.isRoad(h) && this.board.terrainAt(h).id !== 'bridge') {
        const c = center(h);
        g.fillStyle(color, alpha).fillCircle(c.x, c.y, width / 2);
      }
    }
    // River banks: an earthy lip wherever water meets land.
    g.lineStyle(4, 0x4c4128, 0.75);
    for (const h of this.board.all()) {
      if (this.board.terrainAt(h).id !== 'river' && this.board.terrainAt(h).id !== 'bridge') continue;
      const pts = corners(h, HEX_R - 1);
      neighbors(h).forEach((n, i) => {
        if (!this.board.inBounds(n)) return;
        const id = this.board.terrainAt(n).id;
        if (id === 'river' || id === 'bridge') return;
        const [p, q] = EDGE[i];
        g.lineBetween(pts[p].x, pts[p].y, pts[q].x, pts[q].y);
      });
    }
    // Bridges: wooden deck along the road direction, with rails.
    for (const h of this.board.all()) {
      if (this.board.terrainAt(h).id !== 'bridge') continue;
      const c = center(h);
      const ends = neighbors(h).filter(this.isRoad).map(center);
      const dir = ends.length >= 2 ? new Phaser.Math.Vector2(ends[1].x - ends[0].x, ends[1].y - ends[0].y).normalize() : new Phaser.Math.Vector2(1, 0);
      const side = new Phaser.Math.Vector2(-dir.y, dir.x);
      const quad = (len: number, wide: number) => [
        new Phaser.Math.Vector2(c.x - dir.x * len - side.x * wide, c.y - dir.y * len - side.y * wide),
        new Phaser.Math.Vector2(c.x + dir.x * len - side.x * wide, c.y + dir.y * len - side.y * wide),
        new Phaser.Math.Vector2(c.x + dir.x * len + side.x * wide, c.y + dir.y * len + side.y * wide),
        new Phaser.Math.Vector2(c.x - dir.x * len + side.x * wide, c.y - dir.y * len + side.y * wide),
      ];
      g.fillStyle(0x000000, 0.3).fillPoints(quad(HEX_W / 2 + 2, 15).map((p) => p.add(new Phaser.Math.Vector2(3, 4))), true);
      g.fillStyle(0x8a6a46, 1).fillPoints(quad(HEX_W / 2 + 2, 13), true);
      g.lineStyle(1, 0x5c4630, 0.8);
      for (let k = -6; k <= 6; k++) {
        const x = c.x + dir.x * k * 6, y = c.y + dir.y * k * 6;
        g.lineBetween(x - side.x * 13, y - side.y * 13, x + side.x * 13, y + side.y * 13);
      }
      g.lineStyle(3, 0x4a3724, 1);
      for (const s of [-13, 13]) g.lineBetween(c.x - dir.x * (HEX_W / 2 + 2) + side.x * s, c.y - dir.y * (HEX_W / 2 + 2) + side.y * s,
        c.x + dir.x * (HEX_W / 2 + 2) + side.x * s, c.y + dir.y * (HEX_W / 2 + 2) + side.y * s);
    }
  }

  private drawTrees(reducedMotion: boolean) {
    let seed = 7;
    const rand = () => ((seed = (seed * 16807) % 2147483647) / 2147483647);
    for (const h of this.board.all()) {
      if (this.board.terrainAt(h).id !== 'forest') continue;
      const c = center(h);
      const count = 4 + Math.floor(rand() * 2);
      for (let i = 0; i < count; i++) {
        const a = rand() * Math.PI * 2, d = 6 + rand() * 20;
        const tree = this.scene.add.image(c.x + Math.cos(a) * d, c.y + Math.sin(a) * d * 0.85, `tree-${i % 3}`)
          .setScale((0.8 + rand() * 0.45) / ART_SCALE).setDepth(DEPTH.deco + 0.1);
        if (!reducedMotion)
          this.scene.tweens.add({ targets: tree, angle: { from: -2.5, to: 2.5 }, duration: 1700 + rand() * 1200, yoyo: true, repeat: -1, ease: 'Sine.easeInOut', delay: rand() * 1500 });
      }
    }
  }

  private shimmer() {
    for (const h of this.board.all()) {
      const id = this.board.terrainAt(h).id;
      if (id !== 'river') continue;
      const c = center(h);
      const glint = this.scene.add.image(c.x, c.y, 'spark').setTint(0xbfe3e6).setScale(1.6, 0.5).setAlpha(0).setDepth(DEPTH.deco);
      this.scene.tweens.add({
        targets: glint, alpha: { from: 0, to: 0.35 }, x: c.x + 10, duration: 1600 + ((h.row * 37) % 900),
        yoyo: true, repeat: -1, delay: (h.row * 211 + h.col * 97) % 2400, ease: 'Sine.easeInOut',
      });
    }
  }

  private drawObjective(id: string, h: Hex) {
    const c = center(h);
    const ring = this.scene.add.graphics().setDepth(DEPTH.flag - 0.5);
    ring.lineStyle(2.5, 0xf2e6c8, 0.85);
    for (let i = 0; i < 12; i++) {
      const a1 = (i / 12) * Math.PI * 2, a2 = a1 + Math.PI / 12;
      ring.beginPath().arc(c.x, c.y, 30, a1, a2).strokePath();
    }
    this.flags.set(id, this.scene.add.image(c.x + 14, c.y - 20, 'flag-neutral').setScale(1 / ART_SCALE).setDepth(DEPTH.flag).setOrigin(0.22, 0.92));
    this.scene.add.text(c.x - 24, c.y - 34, id, {
      fontFamily: 'Inter, system-ui, sans-serif', fontSize: '15px', fontStyle: 'bold', color: '#14130f', backgroundColor: '#f2e6c8', padding: { x: 5, y: 1 },
    }).setDepth(DEPTH.flag).setResolution(2);
    this.flagSide.set(id, null);
  }

  /** Raises (or lowers) an objective flag. Animated unless reduced motion. */
  setControl(id: string, side: Side | null, animate: boolean) {
    if (this.flagSide.get(id) === side) return;
    this.flagSide.set(id, side);
    const flag = this.flags.get(id)!;
    const key = side ? `flag-${side}` : 'flag-neutral';
    if (!animate) { flag.setTexture(key); return; }
    this.scene.tweens.add({
      targets: flag, scaleY: 0.05 / ART_SCALE, duration: 220, ease: 'Quad.easeIn',
      onComplete: () => {
        flag.setTexture(key);
        this.scene.tweens.add({ targets: flag, scaleY: 1 / ART_SCALE, duration: 420, ease: 'Back.easeOut' });
      },
    });
  }

  /** Fades fog in/out to match the hexes a side can see. */
  setVisible(visible: Iterable<number>, animate: boolean) {
    const seen = new Set(visible);
    for (const [i, img] of this.fog) {
      const alpha = seen.has(i) ? 0 : 0.78;
      if (Math.abs(img.alpha - alpha) < 0.01) continue;
      this.scene.tweens.killTweensOf(img);
      if (animate) this.scene.tweens.add({ targets: img, alpha, duration: 350 });
      else img.setAlpha(alpha);
    }
  }

  hexName(h: Hex): string {
    const o = this.board.objectiveAt(h);
    return `${this.board.terrainAt(h).name} ${label(h)}${o ? ` · ${o.id} ${o.label}` : ''}`;
  }
}
