import Phaser from 'phaser';
import { ApiError, getTurn } from '../api';
import { app } from '../app';
import { audio } from '../audio';
import { center, MapView, worldSize } from '../map';
import { UnitSprite } from '../units';
import { Board } from '../rules/content';
import type { MatchPayload } from '../types';

const REASON: Record<string, string> = {
  annihilation: 'Every unit on one side was destroyed.',
  vp: 'A side reached the VP target.',
  turnLimit: 'The last turn ended; the higher VP total decides.',
};

/** After-action report: the fog lifts, plus the VP graph, kills/losses and replay of any turn. */
export class DebriefScene extends Phaser.Scene {
  constructor() {
    super('Debrief');
  }

  create(data: { payload: MatchPayload }) {
    const { payload } = data;
    const hud = app.hud;
    hud.hideBattle();
    const state = payload.state;
    const board = new Board(app.content.maps.find((m) => m.id === state.scenario)!, app.content);
    const map = new MapView(this, board, app.reducedMotion);
    map.setVisible(state.visible, false);
    for (const [id, side] of Object.entries(state.control)) map.setControl(id, side, false);
    // Fog is lifted after the match: every surviving unit is shown where it ended.
    for (const u of state.units) {
      const at = center(u.pos);
      new UnitSprite(this, u.id, u.side, u.type, u.maxHp, u.hp, at.x, at.y);
    }
    const { width, height } = worldSize(board);
    this.cameras.main.setZoom(Math.min(this.scale.width / width, this.scale.height / height)).centerOn(width / 2, height / 2);
    this.add.rectangle(width / 2, height / 2, width * 4, height * 4, 0x0b100d, 0.5).setDepth(20);

    const won = state.result === 'blueWins', drew = state.result === 'draw';
    audio.play(won ? 'victory' : drew ? 'capture' : 'defeat');
    if (payload.match.scenario === 'bootcamp') localStorage.setItem('tacticsim.bootcamp', 'done');
    hud.aar({
      result: won ? 'Victory' : drew ? 'Draw' : 'Defeat',
      reason: REASON[state.endReason ?? ''] ?? '',
      vp: [state.vpBlue, state.vpRed], turns: state.vpHistory.length, kills: state.kills, losses: state.losses,
      history: state.vpHistory, vpToWin: state.vpToWin, turnLimit: state.turnLimit,
    });
    let turn = state.vpHistory.length;
    hud.on('replay-turn', async () => {
      try {
        const replay = await getTurn(payload.match.id, turn);
        this.scene.start('Battle', { payload, replayOf: replay });
      } catch (e) {
        hud.toast(e instanceof ApiError ? e.message : 'Could not load that turn.', 'error');
      }
    });
    hud.on('replay-turn-select', (v) => (turn = Number(v)));
    hud.on('menu', () => { hud.modal(null); this.scene.start('Menu'); });
  }
}
