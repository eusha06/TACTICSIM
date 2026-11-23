import Phaser from 'phaser';
import { ART_SCALE } from './art';
import { audio } from './audio';
import { DEPTH } from './map';

type Point = { x: number; y: number };

/** Battle effects. Each returns quickly; durations scale with the scene's tween/time timeScale. */
export class Effects {
  constructor(private readonly scene: Phaser.Scene) {}

  tracer(from: Point, to: Point, color = 0xffe59a) {
    audio.play('shot');
    const g = this.scene.add.graphics().setDepth(DEPTH.fx);
    g.lineStyle(2.5, color, 1).lineBetween(from.x, from.y, to.x, to.y);
    g.lineStyle(7, color, 0.25).lineBetween(from.x, from.y, to.x, to.y);
    this.scene.tweens.add({ targets: g, alpha: 0, duration: 260, onComplete: () => g.destroy() });
    this.flash(from, 0xfff1b8, 0.9);
    this.flash(to, 0xffc477, 0.6);
  }

  flash(at: Point, tint: number, scale: number) {
    const f = this.scene.add.image(at.x, at.y, 'spark').setTint(tint).setScale(scale * 2.2).setDepth(DEPTH.fx).setBlendMode(Phaser.BlendModes.ADD);
    this.scene.tweens.add({ targets: f, alpha: 0, scale: scale * 3.4, duration: 220, onComplete: () => f.destroy() });
  }

  /** Parabolic shell arc, whistle, then an explosion and a scorch decal. */
  shell(from: Point | null, to: Point, duration: number, onImpact?: () => void) {
    const start = from ?? { x: to.x - 220, y: to.y - 320 };
    if (from) { audio.play('cannon'); this.flash(from, 0xffe0a0, 1.2); }
    audio.play('whistle');
    const shell = this.scene.add.image(start.x, start.y, 'shell').setScale(1 / ART_SCALE).setDepth(DEPTH.fx);
    const peak = Math.min(start.y, to.y) - 140;
    const curve = new Phaser.Curves.QuadraticBezier(new Phaser.Math.Vector2(start.x, start.y),
      new Phaser.Math.Vector2((start.x + to.x) / 2, peak), new Phaser.Math.Vector2(to.x, to.y));
    const t = { v: 0 };
    this.scene.tweens.add({
      targets: t, v: 1, duration, ease: 'Sine.easeIn',
      onUpdate: () => { const p = curve.getPoint(t.v); shell.setPosition(p.x, p.y); },
      onComplete: () => { shell.destroy(); this.explosion(to); onImpact?.(); },
    });
  }

  explosion(at: Point) {
    audio.play('explosion');
    this.scene.add.image(at.x + Phaser.Math.Between(-4, 4), at.y + 6, 'scorch').setScale(1.2 / ART_SCALE).setDepth(DEPTH.decal).setAlpha(0.85);
    this.flash(at, 0xffb45a, 1.8);
    const burst = this.scene.add.particles(at.x, at.y, 'spark', {
      speed: { min: 60, max: 220 }, lifespan: 420, scale: { start: 0.7, end: 0 }, tint: [0xffe39a, 0xff9a3c, 0xd0532a],
      blendMode: 'ADD', emitting: false,
    }).setDepth(DEPTH.fx);
    burst.explode(26);
    this.smoke(at, 10);
    this.scene.time.delayedCall(1400, () => burst.destroy());
    this.scene.cameras.main.shake(140, 0.004);
  }

  smoke(at: Point, count: number) {
    const puff = this.scene.add.particles(at.x, at.y, 'smoke', {
      speedY: { min: -40, max: -15 }, speedX: { min: -14, max: 14 }, lifespan: 1600, alpha: { start: 0.55, end: 0 },
      scale: { start: 0.35, end: 1.1 }, tint: 0x6f6a62, emitting: false,
    }).setDepth(DEPTH.fx);
    puff.explode(count);
    this.scene.time.delayedCall(1800, () => puff.destroy());
  }

  /** Smoke and a wreck decal (cosmetic only) where a unit was destroyed. */
  wreck(at: Point) {
    this.scene.add.image(at.x, at.y + 3, 'wreck').setScale(0.75 / ART_SCALE).setDepth(DEPTH.decal).setAngle(Phaser.Math.Between(-30, 30));
    this.smoke(at, 18);
    const lingering = this.scene.add.particles(at.x, at.y, 'smoke', {
      speedY: { min: -28, max: -10 }, lifespan: 1800, alpha: { start: 0.35, end: 0 }, scale: { start: 0.2, end: 0.7 },
      tint: 0x55524c, frequency: 260,
    }).setDepth(DEPTH.fx);
    this.scene.time.delayedCall(4200, () => lingering.destroy());
  }

  number(at: Point, text: string, color = '#ffd38a') {
    const label = this.scene.add.text(at.x, at.y - 24, text, {
      fontFamily: 'Inter, system-ui, sans-serif', fontSize: '20px', fontStyle: 'bold', color, stroke: '#0c0f0c', strokeThickness: 5,
    }).setOrigin(0.5).setDepth(DEPTH.top).setResolution(2);
    this.scene.tweens.add({ targets: label, y: at.y - 62, alpha: { from: 1, to: 0 }, duration: 1100, ease: 'Cubic.easeOut', onComplete: () => label.destroy() });
  }

  ping(at: Point, color: number) {
    const ring = this.scene.add.graphics().setDepth(DEPTH.fx);
    const r = { v: 8 };
    this.scene.tweens.add({
      targets: r, v: 40, duration: 700, ease: 'Cubic.easeOut',
      onUpdate: () => ring.clear().lineStyle(3, color, 1 - r.v / 44).strokeCircle(at.x, at.y, r.v),
      onComplete: () => ring.destroy(),
    });
  }

  bump(target: Phaser.GameObjects.Container, toward: Point) {
    const dx = (toward.x - target.x) * 0.18, dy = (toward.y - target.y) * 0.18;
    this.scene.tweens.add({ targets: target, x: target.x + dx, y: target.y + dy, duration: 110, yoyo: true, ease: 'Quad.easeOut' });
  }
}
