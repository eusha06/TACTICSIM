import Phaser from 'phaser';
import { ApiError, submitTurn } from '../api';
import { app, newCommandId } from '../app';
import { HEX_R } from '../art';
import { audio } from '../audio';
import { Effects } from '../effects';
import { center, corners, DEPTH, hexAt, MapView, worldSize } from '../map';
import { describeOrder, OrderBook, Preview } from '../planner';
import { Board } from '../rules/content';
import { distance, label, same, type Hex } from '../rules/hex';
import { cover, visionRadius } from '../rules/rules';
import type { MatchPayload, MatchView, Order, OrderType, ReplayView, SimEvent, TickView, UnitView } from '../types';
import { UnitSprite } from '../units';

type Phase = 'planning' | 'executing' | 'debrief' | 'replay';
type Mode = 'move' | 'attack' | 'barrage';
interface BattleData { payload: MatchPayload; tutorial?: boolean; replayOf?: MatchPayload }

const TICK_MS = 600;
const esc = (s: string) => s.replace(/[&<>"']/g, (c) => `&#${c.charCodeAt(0)};`);

interface TutorialStep { turn: number; title: string; text: string; doneText: string; focus: Hex | ((s: BattleScene) => Hex | null); done: (s: BattleScene) => boolean }

export class BattleScene extends Phaser.Scene {
  board!: Board;
  map!: MapView;
  fx!: Effects;
  payload!: MatchPayload;
  view!: MatchView;
  preview!: Preview;
  readonly book = new OrderBook();
  private sprites = new Map<number, UnitSprite>();
  private ghosts = new Map<number, UnitSprite>();
  private reachG!: Phaser.GameObjects.Graphics;
  private planG!: Phaser.GameObjects.Graphics;
  private hoverG!: Phaser.GameObjects.Graphics;
  private threatG!: Phaser.GameObjects.Graphics;
  private focusG!: Phaser.GameObjects.Graphics;
  private pathLabel!: Phaser.GameObjects.Text;
  private minimap!: Phaser.Cameras.Scene2D.Camera;
  private viewRect!: Phaser.GameObjects.Graphics;
  private phase: Phase = 'planning';
  private mode: Mode = 'move';
  selected: number | null = null;
  private hoverHex: Hex | null = null;
  private cursor: Hex | null = null;
  private threatOn = false;
  private busy = false;
  private pending: { id: string; json: string; version: number } | null = null;
  private lastTurn: { before: MatchView; replay: ReplayView; after: MatchPayload } | null = null;
  private tutorial = false;
  private pb = { paused: false, speed: 1, follow: true, skipping: false, tick: 0, ticks: 9 };
  private pbTimers: Phaser.Time.TimerEvent[] = [];
  private pbTweens: Phaser.Tweens.Tween[] = [];
  private wake: (() => void) | null = null;
  private drag = { down: false, moved: false, x: 0, y: 0, pinch: 0 };
  private lastClick = { time: 0, hex: null as Hex | null };
  private replayOnly: MatchPayload | null = null;
  private keyHandler = (e: KeyboardEvent) => this.onKey(e);

  constructor() {
    super('Battle');
  }

  // ---------------------------------------------------------------- setup
  create(data: BattleData) {
    const hud = app.hud;
    hud.modal(null);
    this.sprites.clear();
    this.ghosts.clear();
    this.book.clear();
    this.phase = 'planning';
    this.selected = null;
    this.tutorial = !!data.tutorial && data.payload.match.scenario === 'bootcamp';
    this.replayOnly = data.replayOf ?? null;
    const state = data.payload.state;
    this.board = new Board(app.content.maps.find((m) => m.id === state.scenario)!, app.content);
    this.map = new MapView(this, this.board, app.reducedMotion);
    this.fx = new Effects(this);
    this.threatG = this.add.graphics().setDepth(DEPTH.overlay - 0.2);
    this.reachG = this.add.graphics().setDepth(DEPTH.overlay);
    this.planG = this.add.graphics().setDepth(DEPTH.overlay + 0.1);
    this.hoverG = this.add.graphics().setDepth(DEPTH.overlay + 0.2);
    this.focusG = this.add.graphics().setDepth(DEPTH.overlay + 0.3);
    this.pathLabel = this.add.text(0, 0, '', {
      fontFamily: 'Inter, system-ui, sans-serif', fontSize: '13px', fontStyle: 'bold', color: '#14130f',
      backgroundColor: '#f2e6c8', padding: { x: 6, y: 3 },
    }).setDepth(DEPTH.top).setResolution(2).setVisible(false);
    this.setupCamera();
    this.setupInput();
    this.wireHud();
    this.setPayload(data.payload);
    if (this.replayOnly) void this.replayFinished();
    else this.startPlanning();
    window.addEventListener('keydown', this.keyHandler);
    this.events.once('shutdown', () => {
      window.removeEventListener('keydown', this.keyHandler);
      this.time.timeScale = 1;
      this.tweens.timeScale = 1;
    });
  }

  private setupCamera() {
    const { width, height } = worldSize(this.board);
    const cam = this.cameras.main;
    cam.setBounds(-width * 0.4, -height * 0.4, width * 1.8, height * 1.8);
    const fit = () => {
      const narrow = this.scale.width < 760;
      const usableW = this.scale.width - (narrow ? 0 : 330), usableH = this.scale.height - (narrow ? 190 : 140);
      cam.setZoom(Phaser.Math.Clamp(Math.min(usableW / width, usableH / height), 0.45, 1.6));
      cam.centerOn(width / 2 + (narrow ? 0 : 150 / cam.zoom), height / 2 - (narrow ? 40 : 0) / cam.zoom);
      const mw = narrow ? 0 : 210, mh = Math.round(mw * (height / width));
      this.minimap.setViewport(this.scale.width - mw - 14, this.scale.height - mh - 14, mw, mh).setVisible(!narrow);
      this.minimap.setZoom(mw / width).centerOn(width / 2, height / 2);
    };
    this.minimap = this.cameras.add(0, 0, 10, 10, false, 'minimap').setBackgroundColor(0x0b100d);
    this.viewRect = this.add.graphics().setDepth(DEPTH.top);
    cam.ignore(this.viewRect);
    fit();
    this.scale.on('resize', fit);
    this.events.once('shutdown', () => this.scale.off('resize', fit));
  }

  update() {
    const cam = this.cameras.main;
    const v = cam.worldView;
    this.viewRect.clear().lineStyle(6, 0xf2e6c8, 0.9).strokeRect(v.x, v.y, v.width, v.height);
  }

  private setupInput() {
    this.input.addPointer(1);
    const cam = this.cameras.main;
    this.input.on('pointerdown', (p: Phaser.Input.Pointer) => {
      if (this.minimap.visible && this.minimap.worldView && this.inMinimap(p)) {
        const w = this.minimap.getWorldPoint(p.x, p.y);
        cam.pan(w.x, w.y, 300, 'Sine.easeOut');
        return;
      }
      this.drag = { down: true, moved: false, x: p.x, y: p.y, pinch: 0 };
    });
    this.input.on('pointermove', (p: Phaser.Input.Pointer) => {
      const p1 = this.input.pointer1, p2 = this.input.pointer2;
      if (p1.isDown && p2.isDown) {
        const d = Phaser.Math.Distance.Between(p1.x, p1.y, p2.x, p2.y);
        if (this.drag.pinch) this.zoomBy(d / this.drag.pinch, (p1.x + p2.x) / 2, (p1.y + p2.y) / 2);
        this.drag.pinch = d;
        this.drag.moved = true;
        return;
      }
      if (this.drag.down && p.isDown) {
        if (Math.abs(p.x - this.drag.x) + Math.abs(p.y - this.drag.y) > 6) this.drag.moved = true;
        if (this.drag.moved) {
          cam.scrollX -= (p.x - p.prevPosition.x) / cam.zoom;
          cam.scrollY -= (p.y - p.prevPosition.y) / cam.zoom;
          return;
        }
      }
      if (this.inMinimap(p)) return;
      const w = p.positionToCamera(cam) as Phaser.Math.Vector2;
      this.cursor = null;
      this.setHover(hexAt(this.board, w.x, w.y), p.x, p.y);
    });
    this.input.on('pointerup', (p: Phaser.Input.Pointer) => {
      const wasDrag = this.drag.moved;
      this.drag.down = false;
      if (wasDrag || this.inMinimap(p)) return;
      const w = p.positionToCamera(cam) as Phaser.Math.Vector2;
      const h = hexAt(this.board, w.x, w.y);
      const now = this.time.now;
      if (h && same(this.lastClick.hex, h) && now - this.lastClick.time < 320) {
        cam.pan(w.x, w.y, 280, 'Sine.easeOut'); // double-click centers
        this.lastClick.time = 0;
        return;
      }
      this.lastClick = { time: now, hex: h };
      if (h) this.click(h);
    });
    this.input.on('wheel', (p: Phaser.Input.Pointer, _o: unknown, _dx: number, dy: number) => this.zoomBy(dy > 0 ? 0.9 : 1.1, p.x, p.y));
  }

  private inMinimap(p: Phaser.Input.Pointer) {
    const m = this.minimap;
    return m.visible && p.x >= m.x && p.x <= m.x + m.width && p.y >= m.y && p.y <= m.y + m.height;
  }

  private zoomBy(factor: number, sx: number, sy: number) {
    const cam = this.cameras.main;
    const before = cam.getWorldPoint(sx, sy);
    cam.setZoom(Phaser.Math.Clamp(cam.zoom * factor, 0.4, 2.4));
    const after = cam.getWorldPoint(sx, sy);
    cam.scrollX += before.x - after.x;
    cam.scrollY += before.y - after.y;
  }

  private wireHud() {
    const hud = app.hud;
    hud.on('menu', () => { if (this.phase !== 'executing') { hud.modal(null); this.scene.start('Menu'); } });
    hud.on('rules', () => hud.rules(app.content));
    hud.on('close', () => hud.modal(null));
    hud.on('sound', () => hud.sound());
    hud.on('volume', (v) => audio.setVolume(Number(v) / 100));
    hud.on('mute', () => { audio.setVolume(audio.volume, !audio.muted); this.refreshTopbar(); });
    hud.on('threat', () => this.toggleThreat());
    hud.on('execute', () => void this.execute());
    hud.on('undo', () => { if (this.book.undo()) this.ordersChanged(); });
    hud.on('redo', () => { if (this.book.redo()) this.ordersChanged(); });
    hud.on('select', (id) => this.select(Number(id), true));
    hud.on('order', (type) => this.orderButton(type as OrderType));
    hud.on('pause', () => this.togglePause());
    hud.on('speed', (s) => this.setSpeed(Number(s)));
    hud.on('skip', () => this.skip());
    hud.on('follow', () => { this.pb.follow = !this.pb.follow; this.refreshPlayback(); });
    hud.on('next', () => this.next());
    hud.on('replay-last', () => void this.replayLast());
    hud.on('tutorial-skip', () => { this.tutorial = false; localStorage.setItem('tacticsim.bootcamp', 'skipped'); this.refreshTutorial(); });
  }

  // ---------------------------------------------------------------- state
  private setPayload(payload: MatchPayload) {
    this.payload = payload;
    this.view = payload.state;
    this.preview = new Preview(this.board, this.view);
    const alive = new Set(this.view.units.map((u) => u.id));
    for (const [id, s] of this.sprites) if (!alive.has(id)) { s.destroy(); this.sprites.delete(id); }
    for (const u of this.view.units) {
      const at = center(u.pos);
      const sprite = this.sprites.get(u.id) ?? this.spawn(u.id, u.side, u.type, u.hp, u.pos);
      sprite.container.setPosition(at.x, at.y).setAlpha(1);
      sprite.setHp(u.hp);
      sprite.setOrder(null);
    }
    for (const g of this.ghosts.values()) g.destroy();
    this.ghosts.clear();
    for (const m of this.view.lastSeen) {
      if (this.view.visible.includes(this.board.index(m.pos))) continue;
      const at = center(m.pos);
      this.ghosts.set(m.unitId, new UnitSprite(this, m.unitId, this.view.viewer === 'blue' ? 'red' : 'blue', m.type, this.board.unit(m.type).hp, m.hp, at.x, at.y, true));
    }
    this.map.setVisible(this.view.visible, false);
    for (const [id, side] of Object.entries(this.view.control)) this.map.setControl(id, side, false);
    this.refreshTopbar();
  }

  private spawn(id: number, side: 'blue' | 'red', type: string, hp: number, pos: Hex): UnitSprite {
    const at = center(pos);
    const sprite = new UnitSprite(this, id, side, type, this.board.unit(type).hp, hp, at.x, at.y);
    this.sprites.set(id, sprite);
    return sprite;
  }

  private refreshTopbar() {
    const v = this.view;
    const phase = { planning: 'PLANNING', executing: 'EXECUTING', debrief: 'DEBRIEF', replay: 'REPLAY' }[this.phase];
    app.hud.topbar({
      turn: Math.min(v.turn, v.turnLimit), turnLimit: v.turnLimit, phase, vpBlue: v.vpBlue, vpRed: v.vpRed, vpToWin: v.vpToWin,
      objectives: this.board.map.objectives.map((o) => ({ id: o.id, label: o.label, side: v.control[o.id] ?? null })),
      difficulty: this.payload.match.scenario === 'bootcamp' ? 'Boot Camp' : this.payload.match.difficulty,
    });
  }

  private own(): UnitView[] {
    return this.view.units.filter((u) => u.side === this.view.viewer).sort((a, b) => a.id - b.id);
  }
  private unitAt(h: Hex) {
    return this.view.units.find((u) => same(u.pos, h));
  }

  // ---------------------------------------------------------------- planning
  private startPlanning() {
    this.phase = 'planning';
    this.book.clear();
    this.pending = null;
    this.mode = 'move';
    app.hud.playback(null);
    app.hud.ticker(null);
    this.refreshTopbar();
    const first = this.own()[0];
    this.select(first && this.tutorial ? (this.own().find((u) => u.type === 'armor') ?? first).id : null);
    this.ordersChanged();
    this.refreshTutorial();
  }

  private select(id: number | null, center_ = false) {
    if (this.phase !== 'planning') return;
    for (const s of this.sprites.values()) s.setSelected(false);
    this.selected = id;
    this.mode = 'move';
    const unit = this.view.units.find((u) => u.id === id && u.side === this.view.viewer);
    if (!unit) this.selected = null;
    if (unit) {
      this.sprites.get(unit.id)?.setSelected(true);
      audio.play('select');
      if (center_) { const c = center(unit.pos); this.cameras.main.pan(c.x, c.y, 260, 'Sine.easeOut'); }
    }
    this.drawReach();
    this.refreshUnitCard();
    this.refreshOrders();
    this.redrawHover();
  }

  private selectedUnit() {
    return this.own().find((u) => u.id === this.selected);
  }

  private drawReach() {
    const g = this.reachG.clear();
    const unit = this.selectedUnit();
    if (!unit || this.phase !== 'planning') return;
    const def = this.board.unit(unit.type);
    if (this.mode === 'barrage') {
      for (const h of this.board.all()) {
        const d = distance(unit.pos, h);
        if (d < def.minRange || d > def.maxRange) continue;
        const seen = this.preview.visible.has(this.board.index(h));
        g.fillStyle(seen ? 0xf0a050 : 0x7a5a3a, seen ? 0.26 : 0.1).fillPoints(corners(h, HEX_R - 3), true);
      }
      return;
    }
    for (const p of this.preview.reach(unit).values()) {
      const alpha = Math.max(0.1, 0.36 - p.cost * 0.035);
      g.fillStyle(p.roadMarch ? 0xf3d77a : 0xd7e27a, alpha).fillPoints(corners(p.dest, HEX_R - 3), true);
    }
    g.lineStyle(2, 0xf7e7a1, 0.9).strokePoints(corners(unit.pos, HEX_R - 2), true);
  }

  private ordersChanged() {
    for (const u of this.own()) this.sprites.get(u.id)?.setOrder(this.book.get(u.id)?.type ?? null);
    this.drawPlans();
    this.refreshOrders();
    this.refreshUnitCard();
    this.redrawHover();
    this.refreshTutorial();
  }

  private drawPlans() {
    const g = this.planG.clear();
    if (this.phase !== 'planning') return;
    for (const u of this.own()) {
      const o = this.book.get(u.id);
      if (!o) continue;
      const selected = u.id === this.selected;
      const color = selected ? 0xf7e7a1 : 0x9cc3ea;
      if (o.path?.length) this.polyline(g, [u.pos, ...o.path], color, selected ? 4 : 3, selected ? 0.95 : 0.6);
      const target = this.view.units.find((t) => t.id === o.targetId);
      if (target) {
        const from = center(o.path?.length ? o.path[o.path.length - 1] : u.pos), to = center(target.pos);
        g.lineStyle(2, 0xf07a5a, 0.7).lineBetween(from.x, from.y, to.x, to.y);
        g.lineStyle(2.5, 0xf07a5a, 0.95).strokeCircle(to.x, to.y, 20).lineBetween(to.x - 26, to.y, to.x - 14, to.y).lineBetween(to.x + 14, to.y, to.x + 26, to.y);
      }
      if (o.type === 'barrage' && o.targetHex) {
        const c = center(o.targetHex);
        g.lineStyle(3, 0xf0a050, 0.95).strokeCircle(c.x, c.y, 24).strokeCircle(c.x, c.y, 12);
      }
      if (o.type === 'overwatch') {
        const c = center(u.pos);
        g.lineStyle(2, 0x9fd0ff, 0.5).strokeCircle(c.x, c.y, this.board.unit(u.type).maxRange * HEX_R * 1.65);
      }
    }
  }

  private polyline(g: Phaser.GameObjects.Graphics, hexes: Hex[], color: number, width: number, alpha: number) {
    const pts = hexes.map(center);
    g.lineStyle(width + 3, 0x0d110e, alpha * 0.5);
    for (let i = 1; i < pts.length; i++) g.lineBetween(pts[i - 1].x, pts[i - 1].y, pts[i].x, pts[i].y);
    g.lineStyle(width, color, alpha);
    for (let i = 1; i < pts.length; i++) g.lineBetween(pts[i - 1].x, pts[i - 1].y, pts[i].x, pts[i].y);
    const a = pts[pts.length - 2], b = pts[pts.length - 1];
    if (!a) return;
    const angle = Math.atan2(b.y - a.y, b.x - a.x);
    const tip = (d: number, s: number) => new Phaser.Math.Vector2(b.x - Math.cos(angle) * d + Math.cos(angle + Math.PI / 2) * s, b.y - Math.sin(angle) * d + Math.sin(angle + Math.PI / 2) * s);
    g.fillStyle(color, alpha).fillPoints([tip(-2, 0), tip(14, 8), tip(14, -8)], true);
  }

  private setHover(h: Hex | null, sx: number, sy: number) {
    if (same(h, this.hoverHex) && h) { this.positionHover(sx, sy); return; }
    this.hoverHex = h;
    this.redrawHover(sx, sy);
  }

  private hoverScreen = { x: 0, y: 0 };
  private positionHover(sx: number, sy: number) {
    this.hoverScreen = { x: sx, y: sy };
    const el = document.getElementById('hover');
    if (el && !el.hidden) app.hud.hover(el.innerHTML, sx, sy);
  }

  /** Path arrow, tick count, contact / road-march badges, or the combat card with its LOS line. */
  private redrawHover(sx = this.hoverScreen.x, sy = this.hoverScreen.y) {
    this.hoverScreen = { x: sx, y: sy };
    const g = this.hoverG.clear();
    this.pathLabel.setVisible(false);
    const h = this.cursor ?? this.hoverHex;
    if (this.cursor) g.lineStyle(3, 0xffffff, 0.95).strokePoints(corners(this.cursor, HEX_R - 1), true);
    if (!h || this.phase !== 'planning') { app.hud.hover(null); return; }
    const unit = this.selectedUnit();
    const there = this.unitAt(h);
    const terrain = this.board.terrainAt(h);
    const hexLine = `<p class="muted">${esc(this.map.hexName(h))} · cover ${terrain.footCost === null ? '—' : terrain.cover}${terrain.blocksLos ? ' · blocks sight' : ''}</p>`;
    const ghost = this.view.lastSeen.find((m) => same(m.pos, h) && this.ghosts.has(m.unitId));

    if (unit && this.mode === 'barrage') {
      const problem = this.preview.barrageProblem(unit, h);
      const risk = this.preview.friendlyFireRisk(h, this.book).filter((u) => u.id !== unit.id);
      g.lineStyle(3, problem ? 0xf06b55 : 0xf0a050, 1).strokePoints(corners(h, HEX_R - 2), true);
      app.hud.hover(`<h3>Barrage ${label(h)}</h3><p>Shell lands at tick ${app.content.rules.barrageTick} and hits whatever is there, friend or foe.</p>
        ${problem ? `<p class="bad">${esc(problem)}</p>` : '<p class="good">Click to order the barrage.</p>'}
        ${risk.length ? `<p class="warn">⚠ Friendly fire risk: your ${risk.map((u) => this.board.unit(u.type).name).join(', ')} may be on that hex.</p>` : ''}${hexLine}`, sx, sy);
      return;
    }
    if (there && there.side !== this.view.viewer) {
      const def = this.board.unit(there.type);
      let html = `<h3>Enemy ${esc(def.name)}</h3><p>HP ${there.hp}/${there.maxHp} · range ${def.minRange}–${def.maxRange} · ${def.atkFoot}/${def.atkVehicle} vs foot/vehicle</p>`;
      if (unit) {
        const plan = this.preview.attackOrder(unit, there, this.book);
        const from = plan.order?.path?.length ? plan.order.path[plan.order.path.length - 1] : unit.pos;
        const card = this.preview.combat(unit, there, from);
        const a = center(from), b = center(there.pos);
        const blocked = !!card.losBlocker;
        if (!this.board.unit(unit.type).indirect) {
          g.lineStyle(3, blocked ? 0xf06b55 : 0x8fe388, 0.95).lineBetween(a.x, a.y, b.x, b.y);
          if (card.losBlocker) g.lineStyle(3, 0xf06b55, 1).strokePoints(corners(card.losBlocker, HEX_R - 3), true);
        }
        if (plan.order?.path?.length) this.polyline(g, [unit.pos, ...plan.order.path], 0xf7e7a1, 3, 0.8);
        html += `<p class="combat">${esc(card.text)}</p><p class="muted">If it Holds this turn it gets +${app.content.rules.holdCover} cover.</p>`;
        html += plan.problem ? `<p class="bad">${esc(plan.problem)}</p>`
          : plan.order?.path?.length ? `<p class="good">Click to Attack: drives to ${label(from)}, fires the first tick it's in range.</p>`
          : `<p class="good">Click to Attack: fires on tick 1.</p>`;
      }
      app.hud.hover(html + hexLine, sx, sy);
      return;
    }
    if (there) {
      const def = this.board.unit(there.type);
      app.hud.hover(`<h3>Your ${esc(def.name)}</h3><p>${esc(describeOrder(this.book.get(there.id), this.board, this.view.units))}</p>${hexLine}`, sx, sy);
      return;
    }
    if (unit) {
      const problem = this.preview.moveProblem(unit, h, this.book);
      const route = this.preview.reach(unit).get(this.board.index(h));
      if (route && !problem) {
        this.polyline(g, [unit.pos, ...route.path], 0xf7e7a1, 4, 0.95);
        const c = center(h);
        this.pathLabel.setText(`${route.cost} tick${route.cost === 1 ? '' : 's'}`).setPosition(c.x + 14, c.y - 30).setVisible(true);
        const contact = this.preview.contactLikely(h);
        app.hud.hover(`<h3>Move to ${label(h)}</h3><p>${route.cost} of ${this.board.unit(unit.type).move} ticks${route.roadMarch ? '' : ''}</p>
          ${route.roadMarch ? `<p class="badge road">Road march +${app.content.rules.roadMarchBonus}</p>` : ''}
          ${contact ? '<p class="warn">⚠ Contact likely: ends next to a known enemy, so the unit will stop there.</p>' : ''}${hexLine}`, sx, sy);
      } else if (!same(h, unit.pos)) {
        app.hud.hover(`<p class="bad">${esc(problem ?? 'Out of reach this turn.')}</p>${hexLine}`, sx, sy);
      } else app.hud.hover(`<p>Click a highlighted hex to move, or press <kbd>3</kbd> to Hold here.</p>${hexLine}`, sx, sy);
      return;
    }
    app.hud.hover(ghost ? `<h3>Last seen: enemy ${esc(this.board.unit(ghost.type).name)}</h3><p>Seen here on turn ${ghost.turn}. It may have moved.</p>${hexLine}` : hexLine, sx, sy);
  }

  private click(h: Hex) {
    if (this.phase !== 'planning') return;
    const unit = this.selectedUnit();
    const there = this.unitAt(h);
    if (there && there.side === this.view.viewer && (this.mode !== 'barrage' || !unit)) {
      if (there.id !== this.selected) this.select(there.id);
      return;
    }
    if (!unit) return;
    if (this.mode === 'barrage') {
      const problem = this.preview.barrageProblem(unit, h);
      if (problem) return app.hud.toast(problem, 'error');
      this.setOrder({ unitId: unit.id, type: 'barrage', targetHex: h });
      this.mode = 'move';
      this.drawReach();
      return;
    }
    if (there && there.side !== this.view.viewer) {
      const plan = this.preview.attackOrder(unit, there, this.book);
      if (plan.problem) return app.hud.toast(plan.problem, 'error');
      this.setOrder(plan.order!);
      return;
    }
    if (this.mode === 'attack') return app.hud.toast('Attack mode: click a visible enemy.', 'info');
    if (same(h, unit.pos)) return;
    const problem = this.preview.moveProblem(unit, h, this.book);
    if (problem) return app.hud.toast(problem, 'error');
    const route = this.preview.reach(unit).get(this.board.index(h))!;
    this.setOrder({ unitId: unit.id, type: 'move', path: route.path });
  }

  private setOrder(order: Order) {
    this.book.set(order);
    audio.play('order');
    this.ordersChanged();
  }

  private orderButton(type: OrderType) {
    const unit = this.selectedUnit();
    if (!unit || this.phase !== 'planning') return;
    const def = this.board.unit(unit.type);
    if (type === 'hold' || type === 'overwatch') { this.setOrder({ unitId: unit.id, type }); return; }
    if (type === 'barrage' && !def.indirect) return app.hud.toast('Only Artillery can Barrage.', 'error');
    this.mode = type === 'barrage' ? 'barrage' : type === 'attack' ? 'attack' : 'move';
    if (type === 'attack' && this.preview.enemies.length === 0) app.hud.toast('No enemy in sight to attack.', 'info');
    this.drawReach();
    this.refreshUnitCard();
    this.redrawHover();
  }

  private refreshUnitCard() {
    const unit = this.selectedUnit();
    if (!unit || this.phase !== 'planning') { app.hud.unitCard(null); return; }
    const def = this.board.unit(unit.type);
    const order = this.book.get(unit.id);
    const t = this.board.terrainAt(unit.pos);
    const hill = !!t.hill;
    app.hud.unitCard({
      name: def.name, side: unit.side, hp: unit.hp, maxHp: unit.maxHp, special: def.special,
      order: `Order: ${describeOrder(order, this.board, this.view.units)}`,
      stats: [
        ['Move', `${def.move}`, `Ticks of movement per turn. Open and road cost 1; see Rules for terrain.`],
        ['Range', `${def.minRange}–${def.maxRange}`, `Hexes it can fire at${def.indirect ? ' (indirect: any hex a friendly unit sees)' : ''}.`],
        ['Attack', `${def.atkFoot}/${def.atkVehicle}`, `Damage before cover: ${def.atkFoot} vs Foot, ${def.atkVehicle} vs Vehicle.`],
        ['Vision', `${visionRadius(this.board, unit.type, unit.pos)}`, `Hexes it can see${hill ? ' (+1 on a hill)' : ''}.`],
        ['Cover', `${cover(this.board, unit.type, unit.pos, false)}`, `${t.name} here. Hold adds +${app.content.rules.holdCover}.`],
      ],
      buttons: [
        { type: 'move', label: 'Move', key: '1', enabled: true, active: this.mode === 'move' && order?.type !== 'attack' },
        { type: 'attack', label: 'Attack', key: '2', enabled: this.preview.enemies.length > 0, active: this.mode === 'attack' || order?.type === 'attack', why: this.preview.enemies.length ? undefined : 'No enemy in sight' },
        { type: 'hold', label: 'Hold', key: '3', enabled: true, active: order?.type === 'hold' },
        { type: 'overwatch', label: 'Overwatch', key: '4', enabled: true, active: order?.type === 'overwatch' },
        { type: 'barrage', label: 'Barrage', key: '5', enabled: !!def.indirect, active: this.mode === 'barrage' || order?.type === 'barrage', why: def.indirect ? undefined : 'Artillery only' },
      ],
    });
  }

  private refreshOrders() {
    if (this.phase !== 'planning') { app.hud.orders(null); return; }
    const own = this.own();
    app.hud.orders(own.map((u) => {
      const o = this.book.get(u.id);
      return { id: u.id, name: `${this.board.unit(u.type).name} ${label(u.pos)}`, order: describeOrder(o, this.board, this.view.units), icon: o?.type ?? null, selected: u.id === this.selected };
    }), { ordered: this.book.size, total: own.length, canUndo: this.book.canUndo, canRedo: this.book.canRedo, busy: this.busy });
  }

  private toggleThreat() {
    this.threatOn = !this.threatOn;
    const g = this.threatG.clear();
    if (!this.threatOn || this.phase !== 'planning') return;
    for (const i of this.preview.threat()) {
      const h = this.board.fromIndex(i);
      const c = center(h);
      g.fillStyle(0xd0423a, 0.16).fillPoints(corners(h, HEX_R - 2), true);
      g.lineStyle(1.5, 0xe06050, 0.55);
      for (let k = -2; k <= 2; k++) g.lineBetween(c.x - 18 + k * 9, c.y + 18, c.x + 18 + k * 9, c.y - 18);
    }
    app.hud.toast(this.threatOn ? 'Threat overlay on: red hexes can be hit by known enemies next turn.' : 'Threat overlay off.', 'info', 2500);
  }

  // ---------------------------------------------------------------- tutorial
  private readonly steps: TutorialStep[] = [
    {
      turn: 1, title: 'Step 1 of 3 · Select and move',
      text: 'Your Armor is selected (Tab cycles units). Click the glowing hex F3 on the road to order a Move, then press EXECUTE TURN ▶.',
      doneText: 'Order set. Press EXECUTE TURN ▶ (or Space): both sides move at the same time.',
      focus: { col: 5, row: 2 }, done: (s) => s.book.all().some((o) => o.type === 'move'),
    },
    {
      turn: 2, title: 'Step 2 of 3 · Read the preview, then attack',
      text: 'Hover the enemy Rifle Infantry. The card shows the exact damage: no dice. Click it to order an Attack, then Execute.',
      doneText: 'Attack ordered. Execute and watch the shot.',
      focus: (s) => s.view.units.find((u) => u.side !== s.view.viewer && u.type === 'rifle')?.pos ?? null,
      done: (s) => s.book.all().some((o) => o.type === 'attack'),
    },
    {
      turn: 3, title: 'Step 3 of 3 · Take the objective',
      text: 'Move your Armor onto the Supply Depot X (G4) and Execute. It fires when it arrives. Hold X with no enemy next to it to win.',
      doneText: 'Execute to take the depot.',
      focus: { col: 6, row: 3 }, done: (s) => s.book.all().some((o) => same(o.path?.[o.path.length - 1], { col: 6, row: 3 })),
    },
  ];

  private refreshTutorial() {
    const g = this.focusG.clear();
    this.tweens.killTweensOf(g);
    g.setAlpha(1);
    const step = this.tutorial && this.phase === 'planning' ? this.steps.find((s) => s.turn === this.view.turn) : undefined;
    if (!step) { app.hud.tutorial(this.tutorial && this.phase === 'planning' ? { title: 'Free play', text: 'You know the basics. Take and hold the depot to finish Boot Camp.' } : null); return; }
    const done = step.done(this);
    app.hud.tutorial({ title: step.title, text: done ? step.doneText : step.text, done });
    const focus = typeof step.focus === 'function' ? step.focus(this) : step.focus;
    if (!focus || done) return;
    const c = center(focus);
    g.lineStyle(4, 0xfff3a8, 1).strokePoints(corners(focus, HEX_R - 1), true).lineStyle(10, 0xfff3a8, 0.25).strokeCircle(c.x, c.y, HEX_R);
    if (!app.reducedMotion) this.tweens.add({ targets: g, alpha: 0.35, duration: 650, yoyo: true, repeat: -1 });
  }

  // ---------------------------------------------------------------- keyboard
  private onKey(e: KeyboardEvent) {
    const tag = (document.activeElement?.tagName ?? '').toLowerCase();
    if (e.key === 'Escape') {
      if (app.hud.modalOpen && this.phase === 'planning') { app.hud.modal(null); return; }
      if (this.mode !== 'move') { this.mode = 'move'; this.drawReach(); this.refreshUnitCard(); this.redrawHover(); return; }
      this.cursor = null;
      this.select(null);
      return;
    }
    if (app.hud.modalOpen || ['input', 'select', 'textarea'].includes(tag)) return;
    const onPage = tag === 'body' || tag === 'canvas' || tag === '';
    if (this.phase === 'executing') {
      if (e.key === ' ' || e.key === 'p' || e.key === 'P') { e.preventDefault(); this.togglePause(); }
      return;
    }
    if (this.phase !== 'planning') return;
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'z') { e.preventDefault(); app.hud.emit(e.shiftKey ? 'redo' : 'undo'); return; }
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'y') { e.preventDefault(); app.hud.emit('redo'); return; }
    if (e.key === 'Tab' && onPage) {
      e.preventDefault();
      const own = this.own();
      if (!own.length) return;
      const i = own.findIndex((u) => u.id === this.selected);
      const next = own[(i + (e.shiftKey ? own.length - 1 : 1) + (i < 0 && e.shiftKey ? 1 : 0)) % own.length];
      this.select(next.id, true);
      this.cursor = next.pos;
      this.redrawHover(...this.cursorScreen());
      return;
    }
    if (e.key.startsWith('Arrow') && onPage) {
      e.preventDefault();
      const c = this.cursor ?? this.selectedUnit()?.pos ?? { col: Math.floor(this.board.width / 2), row: Math.floor(this.board.height / 2) };
      const d = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] }[e.key as 'ArrowLeft']!;
      const next = { col: Phaser.Math.Clamp(c.col + d[0], 0, this.board.width - 1), row: Phaser.Math.Clamp(c.row + d[1], 0, this.board.height - 1) };
      this.cursor = next;
      this.redrawHover(...this.cursorScreen());
      return;
    }
    if (e.key === 'Enter' && this.cursor && onPage) { e.preventDefault(); this.click(this.cursor); return; }
    if (e.key === ' ' && onPage) { e.preventDefault(); void this.execute(); return; }
    if (e.key === 't' || e.key === 'T') { this.toggleThreat(); return; }
    const types: OrderType[] = ['move', 'attack', 'hold', 'overwatch', 'barrage'];
    if (/^[1-5]$/.test(e.key)) this.orderButton(types[Number(e.key) - 1]);
  }

  private cursorScreen(): [number, number] {
    if (!this.cursor) return [this.hoverScreen.x, this.hoverScreen.y];
    const c = center(this.cursor), cam = this.cameras.main;
    return [(c.x - cam.worldView.x) * cam.zoom, (c.y - cam.worldView.y) * cam.zoom];
  }

  // ---------------------------------------------------------------- execute
  private async execute() {
    if (this.phase !== 'planning' || this.busy) return;
    const orders = this.book.all();
    const json = JSON.stringify(orders);
    const version = this.payload.match.version;
    // A retry after a network error reuses the same command id, so the server can't apply it twice.
    if (!this.pending || this.pending.json !== json || this.pending.version !== version) this.pending = { id: newCommandId(), json, version };
    this.busy = true;
    this.refreshOrders();
    app.hud.hover(null);
    try {
      const result = await submitTurn(this.payload.match.id, version, orders, this.pending.id);
      this.pending = null;
      this.busy = false;
      await this.playTurn(result);
    } catch (e) {
      this.busy = false;
      const error = e as ApiError;
      app.hud.toast(error.message, 'error', 7000);
      if (error.status === 409 && error.payload?.state) { this.setPayload(error.payload as MatchPayload); this.startPlanning(); }
      else if (error.status === 422 && error.payload?.unitId) this.select(error.payload.unitId, true);
      this.refreshOrders();
    }
  }

  private async playTurn(result: MatchPayload) {
    const before = this.view;
    this.lastTurn = { before, replay: result.replay!, after: result };
    await this.playback(result.replay!, before);
    this.setPayload(result);
    this.showDebrief(result.replay!);
  }

  private showDebrief(replay: ReplayView) {
    this.phase = 'debrief';
    this.refreshTopbar();
    app.hud.playback(null);
    const over = this.payload.match.status !== 'active';
    app.hud.debrief(replay.turn, replay.debrief, over ? 'After-action report' : 'Next turn ▶');
  }

  private next() {
    app.hud.modal(null);
    if (this.payload.match.status !== 'active' || this.replayOnly) { this.scene.start('Debrief', { payload: this.replayOnly ?? this.payload }); return; }
    this.startPlanning();
  }

  private async replayLast() {
    if (!this.lastTurn) { app.hud.toast('Nothing to replay yet.', 'info'); return; }
    app.hud.modal(null);
    const { before, replay, after } = this.lastTurn;
    this.setPayload({ ...after, state: before });
    await this.playback(replay, before);
    this.setPayload(after);
    this.showDebrief(replay);
  }

  /** Replay-only mode (from the after-action report): play a stored turn, then go back. */
  private async replayFinished() {
    const replay = this.replayOnly!.replay!;
    const start = this.startOf(replay);
    this.setPayload({ ...this.replayOnly!, state: start });
    await this.playback(replay, start);
    this.phase = 'replay';
    this.refreshTopbar();
    app.hud.debrief(replay.turn, replay.debrief, 'Back to the report');
  }

  /** Rebuild a turn's starting picture from its first tick (positions before the first move). */
  private startOf(replay: ReplayView): MatchView {
    const first = replay.ticks[0];
    const units: UnitView[] = first.units.map((f) => {
      const move = first.events.find((e) => e.kind === 'move' && e.unitId === f.id && e.from);
      const hurt = first.events.filter((e) => e.targetId === f.id).reduce((n, e) => n + e.damage, 0);
      return { id: f.id, side: f.side, type: f.type, pos: move?.from ?? f.pos, hp: f.hp + hurt, maxHp: this.board.unit(f.type).hp };
    });
    return { ...this.payload.state, units, lastSeen: [], visible: first.visible, turn: replay.turn };
  }

  // ---------------------------------------------------------------- playback
  private async playback(replay: ReplayView, before: MatchView) {
    const hud = app.hud;
    this.phase = 'executing';
    this.select(null);
    this.reachG.clear(); this.planG.clear(); this.hoverG.clear(); this.threatG.clear(); this.focusG.clear();
    this.threatOn = false;
    this.pathLabel.setVisible(false);
    hud.hover(null); hud.orders(null); hud.unitCard(null); hud.tutorial(null); hud.ticker(null);
    for (const s of this.sprites.values()) s.setOrder(null);
    for (const g of this.ghosts.values()) g.container.setVisible(false);
    this.refreshTopbar();
    const names = new Map<number, string>();
    for (const t of replay.ticks) for (const u of t.units) names.set(u.id, `${u.side === 'blue' ? 'Blue' : 'Red'} ${this.board.unit(u.type).name}`);
    for (const u of before.units) names.set(u.id, `${u.side === 'blue' ? 'Blue' : 'Red'} ${this.board.unit(u.type).name}`);
    const name = (id?: number) => (id === undefined ? 'Unseen enemy' : names.get(id) ?? 'Unknown unit');

    if (app.reducedMotion) {
      // Reduced motion: final state at once, plus the full written log.
      for (const t of replay.ticks) for (const e of t.events) { const line = this.describe(e, t.tick, name); if (line) hud.ticker(line, true); }
      for (const e of replay.endEvents) { const line = this.describe(e, 0, name); if (line) hud.ticker(line, true); }
      return;
    }

    this.pb = { ...this.pb, paused: false, skipping: false, tick: 0, ticks: replay.ticks.length };
    this.setSpeed(this.pb.speed);
    const starts = new Map<number, Array<{ id: number; to: Hex; ticks: number }>>();
    for (const t of replay.ticks)
      for (const e of t.events)
        if (e.kind === 'move' && e.from) {
          const begin = Math.max(1, t.tick - e.ticks + 1);
          starts.set(begin, [...(starts.get(begin) ?? []), { id: e.unitId!, to: e.to!, ticks: t.tick - begin + 1 }]);
        }

    for (const tick of replay.ticks) {
      if (this.pb.skipping) break;
      this.pb.tick = tick.tick;
      this.refreshPlayback();
      if (tick.tick === app.content.rules.ticksPerTurn + 1 && tick.events.length) hud.ticker('End of turn — units fire at will');
      for (const m of starts.get(tick.tick) ?? []) {
        const sprite = this.sprites.get(m.id);
        if (!sprite) continue;
        const c = center(m.to);
        sprite.face(c.x, c.y, this, true);
        this.track(this.tweens.add({ targets: sprite.container, x: c.x, y: c.y, duration: TICK_MS * m.ticks * 0.92, ease: m.ticks > 1 ? 'Sine.easeInOut' : 'Quad.easeInOut' }));
      }
      for (const e of tick.events) this.animate(e, tick, name);
      this.map.setVisible(tick.visible, true);
      this.follow(tick);
      await this.wait(TICK_MS);
      if (!this.pb.skipping) this.syncFrames(tick);
    }
    if (!this.pb.skipping) {
      for (const e of replay.endEvents) {
        const line = this.describe(e, 0, name);
        if (line) hud.ticker(line);
        if (e.kind === 'control' && e.objective) {
          this.map.setControl(e.objective, e.side ?? null, true);
          if (e.reason === 'captured') audio.play('capture');
          if (e.reason === 'contested') hud.tipOnce('contested', 'Contested: both sides have a unit on or next to the objective, so nobody scores it.');
        }
      }
      await this.wait(replay.endEvents.length ? TICK_MS * 1.5 : 200);
    }
    for (const t of this.pbTimers) t.remove(false);
    for (const t of this.pbTweens) t.stop();
    this.pbTimers = []; this.pbTweens = [];
    this.setSpeed(1, false);
    this.time.paused = false;
    this.tweens.resumeAll();
  }

  private track(tween: Phaser.Tweens.Tween) { this.pbTweens.push(tween); return tween; }
  private later(ms: number, fn: () => void) { this.pbTimers.push(this.time.delayedCall(ms, fn)); }

  private wait(ms: number): Promise<void> {
    return new Promise((resolve) => {
      if (this.pb.skipping) return resolve();
      const timer = this.time.delayedCall(ms, () => { this.wake = null; resolve(); });
      this.wake = () => { timer.remove(false); this.wake = null; resolve(); };
    });
  }

  private animate(e: SimEvent, tick: TickView, name: (id?: number) => string) {
    const hud = app.hud;
    const viewer = this.view.viewer;
    const line = this.describe(e, tick.tick, name);
    if (line) hud.ticker(line);
    const at = (id?: number, fallback?: Hex) => {
      const s = id !== undefined ? this.sprites.get(id) : undefined;
      return s ? { x: s.x, y: s.y } : fallback ? center(fallback) : null;
    };
    switch (e.kind) {
      case 'move':
        if (!e.from) { // stepped out of the fog
          const frame = tick.units.find((u) => u.id === e.unitId);
          if (frame && !this.sprites.has(frame.id)) this.appear(frame.id, frame.side, frame.type, frame.hp, e.to!);
        }
        break;
      case 'giveWay': {
        const s = this.sprites.get(e.unitId!);
        const c = center(e.to!);
        if (s) this.track(this.tweens.add({ targets: s.container, x: c.x, y: c.y, duration: TICK_MS * 0.5 }));
        break;
      }
      case 'blocked': {
        const s = this.sprites.get(e.unitId!);
        if (s) this.fx.bump(s.container, center(e.to!));
        if (e.reason === 'collision') hud.tipOnce('collision', 'Two units tried to enter the same hex on the same tick, so both stayed where they were.');
        break;
      }
      case 'contact': {
        const p = at(e.unitId, e.to);
        if (p) this.fx.ping(p, 0xf3d77a);
        if (this.sprites.get(e.unitId!)?.side === viewer) hud.tipOnce('contact', 'Your unit stopped: contact with an enemy. Moving units halt when they enter a hex next to an enemy they can see.');
        break;
      }
      case 'spotted':
        if (e.side === viewer) {
          this.fx.ping(center(e.to!), 0xf06b55);
          const frame = tick.units.find((u) => u.id === e.targetId);
          if (frame && !this.sprites.has(frame.id)) this.appear(frame.id, frame.side, frame.type, frame.hp, frame.pos);
          hud.tipOnce('spotted', 'Enemy spotted. If it slips back into the fog it stays on the map as a faded "last seen" marker.');
        }
        break;
      case 'lost':
        if (e.side === viewer) {
          const s = this.sprites.get(e.targetId!);
          if (s) this.track(this.tweens.add({ targets: s.container, alpha: 0, duration: TICK_MS * 0.6, onComplete: () => { s.destroy(); this.sprites.delete(e.targetId!); } }));
        }
        break;
      case 'shot':
        this.later(TICK_MS * 0.3, () => {
          const from = at(e.unitId, e.from), to = at(e.targetId, e.to);
          if (!to) return;
          if (from) { this.sprites.get(e.unitId!)?.face(to.x, to.y, this, true); this.fx.tracer(from, to); }
          else this.fx.flash(to, 0xffc477, 1);
          this.fx.number(to, `−${e.damage}`);
          this.sprites.get(e.targetId!)?.setHp(e.hp);
        });
        break;
      case 'shell':
      case 'barrage': {
        const target = e.to ? center(e.to) : at(e.targetId);
        if (!target) break;
        const from = at(e.unitId, e.from);
        if (e.unitId !== undefined && from) this.sprites.get(e.unitId)?.face(target.x, target.y, this, true);
        this.fx.shell(from, target, TICK_MS * 0.75, () => {
          if (e.targetId !== undefined) { this.fx.number(target, `−${e.damage}`); this.sprites.get(e.targetId)?.setHp(e.hp); }
        });
        if (e.kind === 'barrage') hud.tipOnce('barrage', `Barrage: the shell lands on tick ${app.content.rules.barrageTick} and hits whatever is on that hex, friend or foe.`);
        break;
      }
      case 'destroyed':
        this.later(TICK_MS * 0.8, () => {
          const s = this.sprites.get(e.targetId!);
          const p = s ? { x: s.x, y: s.y } : center(e.to!);
          this.fx.wreck(p);
          if (s) this.track(this.tweens.add({ targets: s.container, alpha: 0, scale: 0.7, duration: 380, onComplete: () => { s.destroy(); this.sprites.delete(e.targetId!); } }));
        });
        break;
    }
  }

  private appear(id: number, side: 'blue' | 'red', type: string, hp: number, pos: Hex) {
    const s = this.spawn(id, side, type, hp, pos);
    s.container.setAlpha(0);
    this.track(this.tweens.add({ targets: s.container, alpha: 1, duration: TICK_MS * 0.5 }));
  }

  /** Snap to the server's authoritative positions at the end of each tick. */
  private syncFrames(tick: TickView) {
    const present = new Set(tick.units.map((u) => u.id));
    for (const f of tick.units) {
      if (f.hp <= 0) continue;
      const s = this.sprites.get(f.id) ?? this.spawn(f.id, f.side, f.type, f.hp, f.pos);
      if (!this.tweens.isTweening(s.container)) { const c = center(f.pos); s.container.setPosition(c.x, c.y).setAlpha(1); }
      s.setHp(f.hp);
    }
    for (const [id, s] of this.sprites)
      if (!present.has(id) && s.side !== this.view.viewer && !this.tweens.isTweening(s.container)) { s.destroy(); this.sprites.delete(id); }
  }

  private follow(tick: TickView) {
    if (!this.pb.follow) return;
    const points = tick.events.flatMap((e) => (e.kind === 'move' || e.kind === 'spotted' ? [] : e.to ? [center(e.to)] : []));
    if (!points.length) return;
    const x = points.reduce((s, p) => s + p.x, 0) / points.length, y = points.reduce((s, p) => s + p.y, 0) / points.length;
    const cam = this.cameras.main;
    const v = cam.worldView;
    if (x < v.x + v.width * 0.2 || x > v.right - v.width * 0.2 || y < v.y + v.height * 0.2 || y > v.bottom - v.height * 0.2)
      cam.pan(x, y, 450, 'Sine.easeInOut');
  }

  private describe(e: SimEvent, tick: number, name: (id?: number) => string): string | null {
    const t = tick > 0 ? (tick > app.content.rules.ticksPerTurn ? 'End of turn — ' : `Tick ${tick} — `) : '';
    const viewer = this.view.viewer;
    switch (e.kind) {
      case 'spotted': return e.side === viewer ? `${t}${name(e.targetId)} spotted at ${label(e.to!)}` : null;
      case 'lost': return e.side === viewer ? `${t}lost sight of ${name(e.targetId)} (last seen ${label(e.to!)})` : null;
      case 'blocked': return `${t}${name(e.unitId)} blocked at ${label(e.to!)}: ${e.reason === 'collision' ? 'two units tried to enter at once' : e.reason === 'enemy' ? 'an enemy is there' : 'a friendly unit holds it'}`;
      case 'contact': return `${t}${name(e.unitId)} stops: contact with ${e.targetId !== undefined ? name(e.targetId) : 'an enemy'}`;
      case 'shot':
      case 'shell': return `${t}${e.unitId !== undefined ? name(e.unitId) : 'Unseen fire'} ${e.kind === 'shell' ? 'shells' : 'fires at'} ${name(e.targetId)}: ${e.damage} damage${e.destroyed ? ' — destroyed!' : ''}`;
      case 'barrage': return `${t}${e.unitId !== undefined ? name(e.unitId) : 'Enemy artillery'} barrage lands on ${label(e.to!)}${e.targetId !== undefined ? `: ${e.damage} damage to ${name(e.targetId)}` : e.damage ? '' : ' — nothing hit that we can see'}`;
      case 'destroyed': return null;
      case 'control': {
        const o = this.board.map.objectives.find((x) => x.id === e.objective)!;
        return e.reason === 'captured' ? `${e.side === viewer ? 'You' : 'The enemy'} took ${o.id} · ${o.label}` : e.reason === 'contested' ? `${o.id} · ${o.label} is contested` : `${o.id} · ${o.label} is unheld`;
      }
      case 'vp': return `${e.side === 'blue' ? 'Blue' : 'Red'} +${e.amount} VP`;
      case 'result': return e.side ? `${e.side === viewer ? 'Victory!' : 'Defeat.'}` : 'Draw.';
      default: return null;
    }
  }

  private refreshPlayback() {
    app.hud.playback(this.phase === 'executing' ? { ...this.pb } : null);
  }

  private togglePause() {
    if (this.phase !== 'executing') return;
    this.pb.paused = !this.pb.paused;
    this.time.paused = this.pb.paused;
    if (this.pb.paused) this.tweens.pauseAll(); else this.tweens.resumeAll();
    this.refreshPlayback();
  }

  private setSpeed(speed: number, remember = true) {
    if (remember) this.pb.speed = speed;
    this.time.timeScale = speed;
    this.tweens.timeScale = speed;
    this.refreshPlayback();
  }

  private skip() {
    if (this.phase !== 'executing') return;
    this.pb.skipping = true;
    for (const t of this.pbTimers) t.remove(false);
    for (const t of this.pbTweens) t.stop();
    this.pbTimers = []; this.pbTweens = [];
    if (this.pb.paused) this.togglePause();
    this.wake?.();
  }
}
