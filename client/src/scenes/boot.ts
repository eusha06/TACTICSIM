import Phaser from 'phaser';
import { connect } from '../api';
import { app } from '../app';
import { allArt } from '../art';

/** Loads textures and the rules manifest. Shows a "waking the server" note on a cold free host. */
export class BootScene extends Phaser.Scene {
  private ready: Promise<void> | null = null;

  constructor() {
    super('Boot');
  }

  preload() {
    app.hud.loading('Setting up the battlefield…');
    this.ready = connect(() => app.hud.loading('Waking the server…', 'The free host sleeps when idle. This can take up to a minute.'))
      .then((content) => { app.content = content; });
    for (const [key, svg] of Object.entries(allArt())) {
      const width = Number(/width="([\d.]+)"/.exec(svg)![1]);
      const height = Number(/height="([\d.]+)"/.exec(svg)![1]);
      this.load.svg(key, URL.createObjectURL(new Blob([svg], { type: 'image/svg+xml' })), { width, height });
    }
  }

  async create() {
    try {
      await this.ready;
      app.hud.loading(null);
      this.scene.start('Menu');
    } catch (error) {
      app.hud.loading('Couldn’t reach the server', `${(error as Error).message} Reload the page to try again.`);
    }
  }
}
