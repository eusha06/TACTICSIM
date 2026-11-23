import Phaser from 'phaser';
import { ApiError, createMatch, getMatch, listMatches } from '../api';
import { app } from '../app';
import { audio } from '../audio';
import { MapView, worldSize } from '../map';
import { Board } from '../rules/content';
import type { MatchPayload, MatchSummary } from '../types';

/** Title menu over a slowly drifting, fogless view of Rivergate. */
export class MenuScene extends Phaser.Scene {
  constructor() {
    super('Menu');
  }

  async create() {
    const hud = app.hud;
    hud.hideBattle();
    const board = new Board(app.content.maps.find((m) => m.id === 'rivergate')!, app.content);
    const map = new MapView(this, board, app.reducedMotion);
    map.setVisible(Array.from({ length: board.width * board.height }, (_, i) => i), false);
    const { width, height } = worldSize(board);
    const cam = this.cameras.main;
    cam.setZoom(Math.max(this.scale.width / width, this.scale.height / height) * 1.15).centerOn(width / 2, height / 2);
    if (!app.reducedMotion)
      this.tweens.add({ targets: cam, scrollX: cam.scrollX + 90, scrollY: cam.scrollY + 30, duration: 22000, yoyo: true, repeat: -1, ease: 'Sine.easeInOut' });
    this.add.rectangle(width / 2, height / 2, width * 4, height * 4, 0x0b100d, 0.55).setDepth(20);

    let matches: MatchSummary[] = [];
    try {
      matches = (await listMatches()).matches;
    } catch (e) {
      hud.toast((e as Error).message, 'error');
    }
    const firstVisit = localStorage.getItem('tacticsim.bootcamp') === null;
    const show = () => hud.menu({ active: matches.find((m) => m.status === 'active' && m.scenario === 'rivergate') ?? null, firstVisit, recent: matches });
    show();

    const go = async (load: () => Promise<MatchPayload>, tutorial = false) => {
      hud.modal(null);
      hud.loading('Deploying units…');
      try {
        const payload = await load();
        hud.loading(null);
        const scene = payload.match.status === 'active' ? 'Battle' : 'Debrief';
        this.scene.start(scene, { payload, tutorial });
      } catch (e) {
        hud.loading(null);
        hud.toast(e instanceof ApiError ? e.message : 'Something went wrong. Please retry.', 'error');
        show();
      }
    };
    hud.on('open', (id) => go(() => getMatch(String(id)), matches.find((m) => m.id === id)?.scenario === 'bootcamp'));
    hud.on('bootcamp', () => go(() => createMatch('bootcamp', 'Recruit'), true));
    hud.on('new', () => go(() => createMatch('rivergate', localStorage.getItem('tacticsim.difficulty') ?? 'Veteran')));
    hud.on('difficulty', (value) => localStorage.setItem('tacticsim.difficulty', String(value)));
    hud.on('rules', () => hud.rules(app.content));
    hud.on('close', show);
    hud.on('sound', () => hud.sound());
    hud.on('volume', (v) => audio.setVolume(Number(v) / 100));
    hud.on('mute', () => audio.setVolume(audio.volume, !audio.muted));
    hud.on('menu', show);
  }
}
