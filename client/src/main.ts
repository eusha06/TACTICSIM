import Phaser from 'phaser';
import './style.css';
import { app } from './app';
import { Hud } from './hud';
import { BattleScene } from './scenes/battle';
import { BootScene } from './scenes/boot';
import { DebriefScene } from './scenes/debrief';
import { MenuScene } from './scenes/menu';

const root = document.getElementById('game-root')!;
app.hud = new Hud(document.body);
document.body.classList.toggle('reduced-motion', app.reducedMotion);

new Phaser.Game({
  type: Phaser.AUTO,
  parent: root,
  backgroundColor: '#17201b',
  antialias: true,
  banner: false,
  disableContextMenu: true,
  scale: { mode: Phaser.Scale.RESIZE, width: window.innerWidth, height: window.innerHeight },
  input: { mouse: { preventDefaultWheel: true } },
  scene: [BootScene, MenuScene, BattleScene, DebriefScene],
});
