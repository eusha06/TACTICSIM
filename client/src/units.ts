import Phaser from 'phaser';
import { ART_SCALE } from './art';
import { DEPTH } from './map';
import type { OrderType, Side } from './types';

/** A miniature on a round base: side ring + shape marker, facing figure, HP pips and an order icon. */
export class UnitSprite {
  readonly container: Phaser.GameObjects.Container;
  private readonly figure: Phaser.GameObjects.Image;
  private readonly pips: Phaser.GameObjects.Graphics;
  private readonly orderIcon: Phaser.GameObjects.Image;
  private readonly halo: Phaser.GameObjects.Graphics;
  private hp: number;

  constructor(scene: Phaser.Scene, readonly id: number, readonly side: Side, readonly type: string,
    private readonly maxHp: number, hp: number, x: number, y: number, ghost = false) {
    this.hp = hp;
    this.halo = scene.add.graphics();
    const base = scene.add.image(0, 0, `base-${side}`).setScale(0.78 / ART_SCALE);
    this.figure = scene.add.image(0, -1, `fig-${type}-${side}`).setScale(0.78 / ART_SCALE);
    this.figure.setAngle(side === 'blue' ? 90 : -90); // face the enemy's edge at the start
    this.pips = scene.add.graphics();
    this.orderIcon = scene.add.image(18, 17, 'icon-hold').setScale(0.75 / ART_SCALE).setVisible(false);
    const parts: Phaser.GameObjects.GameObject[] = [this.halo, base, this.figure, this.pips, this.orderIcon];
    if (ghost) parts.push(scene.add.image(0, 0, 'ghost').setScale(0.8 / ART_SCALE));
    this.container = scene.add.container(x, y, parts).setDepth(ghost ? DEPTH.ghost : DEPTH.unit);
    if (ghost) this.container.setAlpha(0.42);
    this.drawPips();
  }

  get x() { return this.container.x; }
  get y() { return this.container.y; }

  setHp(hp: number) {
    this.hp = hp;
    this.drawPips();
  }

  /** One pip per 2 HP (half-filled for an odd point), above the base. */
  private drawPips() {
    const g = this.pips.clear();
    const count = Math.ceil(this.maxHp / 2);
    const size = 4.2, gap = 1.6;
    const width = count * size + (count - 1) * gap;
    const x0 = -width / 2, y = -31;
    g.fillStyle(0x0d110e, 0.8).fillRoundedRect(x0 - 2.5, y - 2.5, width + 5, size + 5, 3);
    for (let i = 0; i < count; i++) {
      const left = this.hp - i * 2;
      const x = x0 + i * (size + gap);
      g.fillStyle(0x3a3f36, 1).fillRect(x, y, size, size);
      const color = this.hp / this.maxHp > 0.5 ? 0x9fdc7a : this.hp / this.maxHp > 0.25 ? 0xf1c75b : 0xf06b55;
      if (left >= 2) g.fillStyle(color, 1).fillRect(x, y, size, size);
      else if (left === 1) g.fillStyle(color, 1).fillRect(x, y, size / 2, size);
    }
  }

  setOrder(type: OrderType | null) {
    this.orderIcon.setVisible(type !== null);
    if (type) this.orderIcon.setTexture(`icon-${type}`);
  }

  setSelected(selected: boolean, color = 0xf7e7a1) {
    const g = this.halo.clear();
    if (!selected) return;
    g.lineStyle(3, color, 1).strokeCircle(0, 0, 27);
    g.lineStyle(8, color, 0.25).strokeCircle(0, 0, 30);
  }

  /** Turns the figure toward a world point (the miniature faces where it is going or shooting). */
  face(x: number, y: number, scene: Phaser.Scene, animate: boolean) {
    if (Math.abs(x - this.x) + Math.abs(y - this.y) < 1) return;
    const angle = Phaser.Math.RadToDeg(Math.atan2(y - this.y, x - this.x)) + 90;
    const delta = Phaser.Math.Angle.ShortestBetween(this.figure.angle, angle);
    if (animate) scene.tweens.add({ targets: this.figure, angle: this.figure.angle + delta, duration: 160 });
    else this.figure.setAngle(angle);
  }

  destroy() {
    this.container.destroy();
  }
}
