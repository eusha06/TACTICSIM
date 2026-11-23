// DOM overlay HUD: crisp text, real buttons, tooltips and screen-reader updates over the canvas.
import { icon } from './art';
import { audio } from './audio';
import type { Content } from './rules/content';
import type { MatchSummary, Side, VpPoint } from './types';

type Handler = (arg?: unknown) => void;
const esc = (s: string) => s.replace(/[&<>"']/g, (c) => `&#${c.charCodeAt(0)};`);

export interface TopBar {
  turn: number; turnLimit: number; phase: string; vpBlue: number; vpRed: number; vpToWin: number;
  objectives: Array<{ id: string; label: string; side: Side | null }>; difficulty: string;
}
export interface UnitCard {
  name: string; side: Side; hp: number; maxHp: number; stats: Array<[string, string, string]>; special: string;
  order: string; buttons: Array<{ type: string; label: string; key: string; enabled: boolean; active: boolean; why?: string }>;
}
export interface OrderRow { id: number; name: string; order: string; icon: string | null; selected: boolean }

export class Hud {
  readonly root: HTMLElement;
  private readonly handlers = new Map<string, Handler>();
  private toastTimer = 0;

  constructor(parent: HTMLElement) {
    this.root = document.createElement('div');
    this.root.className = 'hud';
    this.root.innerHTML = `
      <div class="loading" id="loading" role="status" aria-live="polite"><div class="spinner"></div><p class="title"></p><p class="sub"></p></div>
      <header class="topbar" id="topbar" hidden></header>
      <aside class="unit-card panel" id="unit-card" hidden aria-live="polite"></aside>
      <aside class="orders panel" id="orders" hidden aria-label="Orders this turn"></aside>
      <div class="hover-card" id="hover" role="tooltip" hidden></div>
      <section class="tutorial panel" id="tutorial" hidden aria-live="polite"></section>
      <footer class="playback" id="playback" hidden></footer>
      <div class="ticker" id="ticker" role="log" aria-live="polite" hidden></div>
      <div class="toast" id="toast" role="alert" hidden></div>
      <div class="modal-backdrop" id="modal" hidden><div class="modal panel" role="dialog" aria-modal="true"></div></div>`;
    parent.appendChild(this.root);
    this.root.addEventListener('click', (e) => {
      const el = (e.target as HTMLElement).closest<HTMLElement>('[data-action]');
      if (!el || (el as HTMLButtonElement).disabled) return;
      audio.play('select');
      this.emit(el.dataset.action!, el.dataset.arg);
    });
    this.root.addEventListener('input', (e) => {
      const el = e.target as HTMLInputElement;
      if (el.dataset.input) this.emit(el.dataset.input, el.value);
    });
  }

  on(name: string, handler: Handler) { this.handlers.set(name, handler); }
  emit(name: string, arg?: unknown) { this.handlers.get(name)?.(arg); }
  private $(id: string) { return this.root.querySelector<HTMLElement>(`#${id}`)!; }

  loading(title: string | null, sub = '') {
    const el = this.$('loading');
    el.hidden = title === null;
    if (title !== null) { el.querySelector('.title')!.textContent = title; el.querySelector('.sub')!.textContent = sub; }
  }

  toast(message: string, kind: 'error' | 'tip' | 'info' = 'info', ms = 5200) {
    const el = this.$('toast');
    el.className = `toast ${kind}`;
    el.innerHTML = `${kind === 'tip' ? '<b>Tip</b> ' : ''}${esc(message)}`;
    el.hidden = false;
    window.clearTimeout(this.toastTimer);
    this.toastTimer = window.setTimeout(() => (el.hidden = true), ms);
    if (kind === 'error') audio.play('error');
  }

  /** Shows a tip once per browser (first time something happens). */
  tipOnce(key: string, message: string) {
    const store = 'tacticsim.tips';
    const seen: string[] = JSON.parse(localStorage.getItem(store) ?? '[]');
    if (seen.includes(key)) return;
    localStorage.setItem(store, JSON.stringify([...seen, key]));
    this.toast(message, 'tip', 7000);
  }

  hideBattle() {
    for (const id of ['topbar', 'unit-card', 'orders', 'hover', 'tutorial', 'playback', 'ticker']) this.$(id).hidden = true;
  }

  topbar(t: TopBar | null) {
    const el = this.$('topbar');
    el.hidden = !t;
    if (!t) return;
    const pips = (n: number, side: Side) => Array.from({ length: t.vpToWin }, (_, i) =>
      `<i class="pip ${i < n ? side : ''}"></i>`).join('');
    el.innerHTML = `
      <button class="brand" data-action="menu" title="Back to menu">RIVERGATE <span>COMMAND</span></button>
      <div class="turn" title="Turn ${t.turn} of ${t.turnLimit}. The match ends after turn ${t.turnLimit}.">TURN <b>${t.turn}</b> / ${t.turnLimit}</div>
      <div class="phase ${t.phase.toLowerCase()}" aria-live="polite">${esc(t.phase)}</div>
      <div class="vp" title="Victory Points. First to ${t.vpToWin} wins. Each objective you control at the end of a turn gives +1.">
        <span class="side blue">BLUE <b>${t.vpBlue}</b></span>
        <span class="bar" aria-hidden="true"><span class="row">${pips(t.vpBlue, 'blue')}</span><span class="row">${pips(t.vpRed, 'red')}</span></span>
        <span class="side red"><b>${t.vpRed}</b> RED</span>
        <span class="target">/ ${t.vpToWin}</span>
      </div>
      <div class="chips" aria-label="Objectives">${t.objectives.map((o) =>
        `<span class="chip ${o.side ?? 'none'}" title="${esc(`${o.id} · ${o.label}: ${o.side ? `held by ${o.side === 'blue' ? 'you' : 'the enemy'}` : 'nobody controls it'}`)}">${icon('flag', 13)}${o.id}</span>`).join('')}</div>
      <span class="difficulty" title="Opponent difficulty">${esc(t.difficulty)}</span>
      <div class="tools">
        <button data-action="threat" title="Threat overlay: hexes known enemies can hit next turn (T)" aria-keyshortcuts="T">Threat</button>
        <button data-action="rules" title="How to play">Rules</button>
        <button data-action="sound" title="Sound settings" aria-label="Sound">${audio.muted ? '🔇' : '🔊'}</button>
      </div>`;
  }

  unitCard(card: UnitCard | null) {
    const el = this.$('unit-card');
    el.hidden = !card;
    if (!card) return;
    el.innerHTML = `
      <div class="card-head"><span class="shape ${card.side}"></span><h2>${esc(card.name)}</h2>
        <span class="hp" title="Health: ${card.hp} of ${card.maxHp}">HP <b>${card.hp}</b>/${card.maxHp}</span></div>
      <dl class="stats">${card.stats.map(([k, v, tip]) => `<div title="${esc(tip)}"><dt>${k}</dt><dd>${v}</dd></div>`).join('')}</dl>
      <p class="special">${esc(card.special)}</p>
      ${card.side === 'blue' ? `<p class="current">${esc(card.order)}</p>
      <div class="order-buttons" role="group" aria-label="Order type">${card.buttons.map((b) =>
        `<button data-action="order" data-arg="${b.type}" class="${b.active ? 'active' : ''}" ${b.enabled ? '' : 'disabled'}
          title="${esc(b.why ?? `${b.label} (${b.key})`)}" aria-keyshortcuts="${b.key}">${icon(b.type, 16)}<span>${b.label}</span><kbd>${b.key}</kbd></button>`).join('')}</div>` : ''}`;
  }

  orders(rows: OrderRow[] | null, info?: { ordered: number; total: number; canUndo: boolean; canRedo: boolean; busy: boolean }) {
    const el = this.$('orders');
    el.hidden = !rows;
    if (!rows || !info) return;
    el.innerHTML = `
      <div class="orders-head"><h2>Orders</h2>
        <span><button data-action="undo" ${info.canUndo ? '' : 'disabled'} title="Undo (Ctrl+Z)">↶</button>
        <button data-action="redo" ${info.canRedo ? '' : 'disabled'} title="Redo (Ctrl+Y)">↷</button></span></div>
      <ol>${rows.map((r) => `<li><button class="row ${r.selected ? 'selected' : ''}" data-action="select" data-arg="${r.id}">
          <span class="ico">${r.icon ? icon(r.icon, 15) : '<span class="dot"></span>'}</span>
          <span class="who">${esc(r.name)}</span><span class="what">${esc(r.order)}</span>
          <span class="check" aria-label="${r.icon ? 'ordered' : 'no order yet'}">${r.icon ? '✓' : ''}</span></button></li>`).join('')}</ol>
      <button class="execute" data-action="execute" ${info.busy ? 'disabled' : ''} aria-keyshortcuts="Space">
        EXECUTE TURN ▶ <small>(${info.ordered}/${info.total} ordered)</small></button>
      <p class="note">${info.ordered < info.total ? `${info.total - info.ordered} unit${info.total - info.ordered === 1 ? '' : 's'} without orders will Hold.` : 'All units have orders.'}</p>`;
  }

  hover(html: string | null, x = 0, y = 0) {
    const el = this.$('hover');
    el.hidden = !html;
    if (!html) return;
    el.innerHTML = html;
    const pad = 16, w = el.offsetWidth, h = el.offsetHeight;
    el.style.left = `${Math.min(window.innerWidth - w - 8, x + pad)}px`;
    el.style.top = `${Math.max(56, Math.min(window.innerHeight - h - 8, y + pad))}px`;
  }

  tutorial(step: { title: string; text: string; done?: boolean } | null) {
    const el = this.$('tutorial');
    el.hidden = !step;
    if (!step) return;
    el.innerHTML = `<p class="eyebrow">BOOT CAMP</p><h2>${esc(step.title)}</h2><p>${esc(step.text)}</p>
      <button data-action="tutorial-skip" class="link">Skip tutorial</button>`;
    el.classList.toggle('done', !!step.done);
  }

  playback(state: { paused: boolean; speed: number; follow: boolean; tick: number; ticks: number } | null) {
    const el = this.$('playback');
    el.hidden = !state;
    this.$('ticker').hidden = !state && !this.$('ticker').classList.contains('log');
    if (!state) return;
    el.innerHTML = `
      <span class="tick">Tick <b>${Math.min(state.tick, state.ticks)}</b> / ${state.ticks}</span>
      <button data-action="pause" title="${state.paused ? 'Resume' : 'Pause'} (P)">${state.paused ? '▶' : '⏸'}</button>
      ${[1, 2, 4].map((s) => `<button data-action="speed" data-arg="${s}" class="${state.speed === s ? 'active' : ''}">${s}×</button>`).join('')}
      <button data-action="skip" title="Skip to the end of the turn">Skip ⏭</button>
      <label class="follow"><input type="checkbox" data-input="follow" ${state.follow ? 'checked' : ''}/> Follow action</label>`;
  }

  ticker(line: string | null, keep = false) {
    const el = this.$('ticker');
    if (line === null) { el.innerHTML = ''; el.hidden = true; el.classList.remove('log'); return; }
    el.hidden = false;
    el.classList.toggle('log', keep);
    const p = document.createElement('p');
    p.textContent = line;
    el.appendChild(p);
    while (!keep && el.children.length > 4) el.firstElementChild!.remove();
    el.scrollTop = el.scrollHeight;
  }

  modal(html: string | null, wide = false) {
    const el = this.$('modal');
    el.hidden = html === null;
    if (html === null) return;
    const box = el.querySelector<HTMLElement>('.modal')!;
    box.classList.toggle('wide', wide);
    box.innerHTML = html;
    (box.querySelector<HTMLElement>('[autofocus]') ?? box.querySelector<HTMLElement>('button'))?.focus();
  }

  get modalOpen() { return !this.$('modal').hidden; }

  debrief(turn: number, lines: string[], next: string) {
    this.modal(`<p class="eyebrow">DEBRIEF · TURN ${turn}</p><h2>What happened</h2>
      <ul class="debrief">${lines.map((l) => `<li>${esc(l)}</li>`).join('')}</ul>
      <div class="actions"><button data-action="replay-last" class="secondary">Replay turn ↺</button>
      <button data-action="next" class="primary" autofocus>${esc(next)}</button></div>`);
  }

  menu(o: { active: MatchSummary | null; firstVisit: boolean; recent: MatchSummary[] }) {
    const difficulty = localStorage.getItem('tacticsim.difficulty') ?? 'Veteran';
    const row = (m: MatchSummary) => `<li><span>${m.scenario === 'bootcamp' ? 'Boot Camp' : 'Rivergate'} · ${esc(m.difficulty)}</span>
      <span>${m.status === 'active' ? `Turn ${m.turn}` : ({ blueWins: 'Victory', redWins: 'Defeat', draw: 'Draw' } as Record<string, string>)[m.status]} · ${m.vpBlue}–${m.vpRed} VP</span>
      <button data-action="open" data-arg="${m.id}" class="link">${m.status === 'active' ? 'Continue' : 'Report'}</button></li>`;
    this.modal(`
      <div class="menu">
        <p class="eyebrow">TACTICSIM v2 · A FICTIONAL WEGO TACTICS SIMULATION</p>
        <h1>Rivergate <span>Command</span></h1>
        <p class="lead">Both sides plan at the same time, then watch eight ticks of battle unfold. No dice: every preview is exact.</p>
        ${o.firstVisit ? `<div class="callout">New here? <b>Boot Camp</b> teaches the whole game in three turns (about 3 minutes).</div>` : ''}
        <div class="menu-actions">
          ${o.active ? `<button class="primary" data-action="open" data-arg="${o.active.id}" autofocus>Continue · Turn ${o.active.turn} (${o.active.vpBlue}–${o.active.vpRed})</button>` : ''}
          <button class="${o.firstVisit && !o.active ? 'primary' : 'secondary'}" data-action="bootcamp" ${o.firstVisit && !o.active ? 'autofocus' : ''}>${icon('flag')} Boot Camp</button>
          <div class="new-battle">
            <button class="${!o.firstVisit && !o.active ? 'primary' : 'secondary'}" data-action="new">${icon('attack')} New battle: Rivergate</button>
            <label>Opponent <select data-input="difficulty">${['Recruit', 'Veteran', 'Commander'].map((d) =>
              `<option ${d === difficulty ? 'selected' : ''}>${d}</option>`).join('')}</select></label>
          </div>
          <button class="secondary" data-action="rules">How to play</button>
        </div>
        ${o.recent.length ? `<details class="recent"><summary>Your battles (${o.recent.length})</summary><ul>${o.recent.slice(0, 8).map(row).join('')}</ul></details>` : ''}
        <p class="foot"><a href="/History">Classic v1 games</a> · <button class="link" data-action="sound">Sound: ${audio.muted ? 'off' : `${Math.round(audio.volume * 100)}%`}</button></p>
      </div>`, true);
  }

  sound() {
    this.modal(`<p class="eyebrow">SETTINGS</p><h2>Sound</h2>
      <label class="volume">Master volume <input type="range" min="0" max="100" value="${Math.round(audio.volume * 100)}" data-input="volume"/></label>
      <label class="check"><input type="checkbox" data-input="mute" ${audio.muted ? 'checked' : ''}/> Mute</label>
      <div class="actions"><button data-action="close" class="primary" autofocus>Done</button></div>`);
  }

  rules(content: Content) {
    const u = (id: string) => content.units.find((x) => x.id === id)!;
    const r = content.rules;
    const map = content.maps.find((m) => m.id === 'rivergate') ?? content.maps[0];
    const cost = content.terrain.filter((t) => t.footCost !== null)
      .map((t) => `${t.name.toLowerCase()} ${t.footCost}${t.vehicleCost !== t.footCost ? ` (vehicles ${t.vehicleCost})` : ''}`).join(', ');
    const cards: Array<[string, string, string]> = [
      ['trophy', 'Win', `Hold objectives to earn VP. First to ${map.vpToWin} VP wins, or destroy every enemy, or lead after turn ${map.turnLimit}. Equal VP is a draw.`],
      ['clock', 'Turn', `Plan: one order per unit while the enemy plans too. Execute: all orders play out over ${r.ticksPerTurn} ticks. Score: +1 VP per objective you control.`],
      ['move', 'Orders', `Move · Attack (move, then fire) · Hold (+${r.holdCover} cover) · Overwatch (fire at the first enemy in range) · Barrage (artillery shell lands at tick ${r.barrageTick}).`],
      ['terrain', 'Move & terrain', `Each hex costs ticks: ${cost}. Rivers only at bridges. Vehicles on an all-road move get +${r.roadMarchBonus}.`],
      ['eye', 'Seeing', `You see only what your units see. Forest, towns and hills block sight; units on hills see over them. A moving unit stops next to a visible enemy.`],
      ['damage', 'Damage', `Damage = attack (vs Foot or vs Vehicle) − cover of the target's hex, minimum ${r.minDamage}. No randomness: previews are exact.`],
      ['flag', 'Objectives', `You control an objective if your unit is on it and no enemy is. Units of both sides on or next to it: contested, nobody scores. Recon and Artillery can't capture.`],
      ['units', 'Units', `Rifle (${u('rifle').atkFoot}/${u('rifle').atkVehicle}) strong in cover · AT (${u('at').atkFoot}/${u('at').atkVehicle}) kills armor · Armor (${u('armor').atkFoot}/${u('armor').atkVehicle}) open-ground hammer · Artillery fires indirectly · Recon sees ${u('recon').vision} hexes.`],
    ];
    const terrain = content.terrain.map((t) => `<tr><th>${t.name}</th><td>${t.footCost ?? '—'}</td><td>${t.vehicleCost ?? '—'}</td><td>${t.footCost === null ? '—' : t.cover}</td><td>${t.blocksLos ? 'yes' : 'no'}</td></tr>`).join('');
    const units = content.units.map((x) => `<tr><th>${x.name}</th><td>${x.hp}</td><td>${x.move}</td><td>${x.minRange}–${x.maxRange}</td><td>${x.atkFoot}</td><td>${x.atkVehicle}</td><td>${x.vision}</td></tr>`).join('');
    this.modal(`<p class="eyebrow">RULES · ${esc(content.rulesVersion)}</p><h2>How to play</h2>
      <div class="cards">${cards.map(([ic, title, text]) => `<article class="rule-card">${icon(ic, 26)}<h3>${title}</h3><p>${text}</p></article>`).join('')}</div>
      <details><summary>Full tables</summary>
        <table><caption>Terrain (move cost in ticks)</caption><thead><tr><th></th><th>Foot</th><th>Vehicle</th><th>Cover</th><th>Blocks sight</th></tr></thead><tbody>${terrain}</tbody></table>
        <table><caption>Units (attack vs Foot / vs Vehicle)</caption><thead><tr><th></th><th>HP</th><th>Move</th><th>Range</th><th>vs Foot</th><th>vs Veh.</th><th>Vision</th></tr></thead><tbody>${units}</tbody></table>
        <p class="keys"><b>Keys:</b> Tab/Shift-Tab cycle units · arrows move the hex cursor · Enter confirms · Esc cancels · 1–5 order type · T threat · Space executes · Ctrl+Z / Ctrl+Y undo/redo.</p>
      </details>
      <div class="actions"><button data-action="close" class="primary" autofocus>Got it</button></div>`, true);
  }

  aar(o: { result: string; reason: string; vp: [number, number]; turns: number; kills: number; losses: number; history: VpPoint[]; vpToWin: number; turnLimit: number }) {
    const w = 520, h = 200, pad = 28;
    const maxVp = Math.max(o.vpToWin, ...o.history.map((p) => Math.max(p.blue, p.red)));
    const x = (t: number) => pad + ((t) / o.turnLimit) * (w - 2 * pad);
    const y = (v: number) => h - pad - (v / maxVp) * (h - 2 * pad);
    const line = (side: 'blue' | 'red') => `M${x(0)} ${y(0)} ` + o.history.map((p) => `L${x(p.turn)} ${y(p[side])}`).join(' ');
    const turns = o.history.map((p) => `<option value="${p.turn}">Turn ${p.turn}</option>`).join('');
    this.modal(`<p class="eyebrow">AFTER-ACTION REPORT</p><h2 class="result ${o.result.toLowerCase()}">${esc(o.result)}</h2><p class="lead">${esc(o.reason)}</p>
      <svg class="vp-graph" viewBox="0 0 ${w} ${h}" role="img" aria-label="Victory points per turn: Blue ${o.vp[0]}, Red ${o.vp[1]}">
        <line x1="${pad}" y1="${y(o.vpToWin)}" x2="${w - pad}" y2="${y(o.vpToWin)}" class="goal"/>
        <text x="${w - pad}" y="${y(o.vpToWin) - 6}" text-anchor="end" class="axis">${o.vpToWin} VP wins</text>
        <line x1="${pad}" y1="${h - pad}" x2="${w - pad}" y2="${h - pad}" class="axis-line"/>
        ${Array.from({ length: o.turnLimit }, (_, i) => `<text x="${x(i + 1)}" y="${h - 8}" text-anchor="middle" class="axis">${i + 1}</text>`).join('')}
        <path d="${line('blue')}" class="blue"/><path d="${line('red')}" class="red"/>
      </svg>
      <dl class="report"><div><dt>Final VP</dt><dd><span class="b">${o.vp[0]}</span> – <span class="r">${o.vp[1]}</span></dd></div>
        <div><dt>Turns</dt><dd>${o.turns}</dd></div><div><dt>Enemy units destroyed</dt><dd>${o.kills}</dd></div><div><dt>Your losses</dt><dd>${o.losses}</dd></div></dl>
      <div class="actions"><label>Watch again <select data-input="replay-turn-select">${turns.replace(`value="${o.history.length}"`, `value="${o.history.length}" selected`)}</select></label>
        <button data-action="replay-turn" class="secondary">Replay ▶</button>
        <button data-action="menu" class="primary" autofocus>Back to menu</button></div>`, true);
  }
}
