// Small synthesized sound set (Web Audio). No sample files, nothing licensed.
export type Sfx = 'select' | 'order' | 'shot' | 'cannon' | 'whistle' | 'explosion' | 'capture' | 'victory' | 'defeat' | 'error';

const STORE = 'tacticsim.audio';

class Audio {
  private ctx: AudioContext | null = null;
  private master: GainNode | null = null;
  volume = 0.6;
  muted = false;

  constructor() {
    try {
      const saved = JSON.parse(localStorage.getItem(STORE) ?? '{}');
      if (typeof saved.volume === 'number') this.volume = saved.volume;
      if (typeof saved.muted === 'boolean') this.muted = saved.muted;
    } catch { /* first visit */ }
  }

  setVolume(volume: number, muted = this.muted) {
    this.volume = Math.min(1, Math.max(0, volume));
    this.muted = muted;
    localStorage.setItem(STORE, JSON.stringify({ volume: this.volume, muted: this.muted }));
    if (this.master) this.master.gain.value = this.muted ? 0 : this.volume;
  }

  /** Browsers only allow audio after a user gesture; the first click wakes it. */
  private context(): AudioContext | null {
    if (!this.ctx) {
      const Ctor = window.AudioContext ?? (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext;
      if (!Ctor) return null;
      this.ctx = new Ctor();
      this.master = this.ctx.createGain();
      this.master.gain.value = this.muted ? 0 : this.volume;
      this.master.connect(this.ctx.destination);
    }
    if (this.ctx.state === 'suspended') void this.ctx.resume();
    return this.ctx;
  }

  play(sfx: Sfx) {
    if (this.muted || this.volume === 0) return;
    const ctx = this.context();
    if (!ctx || !this.master) return;
    const t = ctx.currentTime;
    const tone = (freq: number, start: number, length: number, type: OscillatorType = 'triangle', gain = 0.25, slide?: number) => {
      const osc = ctx.createOscillator();
      const amp = ctx.createGain();
      osc.type = type;
      osc.frequency.setValueAtTime(freq, t + start);
      if (slide) osc.frequency.exponentialRampToValueAtTime(slide, t + start + length);
      amp.gain.setValueAtTime(0.0001, t + start);
      amp.gain.exponentialRampToValueAtTime(gain, t + start + 0.01);
      amp.gain.exponentialRampToValueAtTime(0.0001, t + start + length);
      osc.connect(amp).connect(this.master!);
      osc.start(t + start);
      osc.stop(t + start + length + 0.05);
    };
    const noise = (start: number, length: number, filterFreq: number, gain: number, type: BiquadFilterType = 'lowpass') => {
      const buffer = ctx.createBuffer(1, Math.ceil(ctx.sampleRate * length), ctx.sampleRate);
      const data = buffer.getChannelData(0);
      for (let i = 0; i < data.length; i++) data[i] = (Math.random() * 2 - 1) * (1 - i / data.length) ** 2;
      const src = ctx.createBufferSource();
      const filter = ctx.createBiquadFilter();
      const amp = ctx.createGain();
      src.buffer = buffer;
      filter.type = type;
      filter.frequency.value = filterFreq;
      amp.gain.value = gain;
      src.connect(filter).connect(amp).connect(this.master!);
      src.start(t + start);
    };
    switch (sfx) {
      case 'select': tone(660, 0, 0.08, 'sine', 0.15); break;
      case 'order': tone(520, 0, 0.07, 'triangle', 0.15); tone(780, 0.07, 0.09, 'triangle', 0.15); break;
      case 'error': tone(220, 0, 0.14, 'square', 0.08); tone(180, 0.12, 0.16, 'square', 0.08); break;
      case 'shot': noise(0, 0.12, 2400, 0.5, 'highpass'); tone(140, 0, 0.08, 'square', 0.12, 60); break;
      case 'cannon': noise(0, 0.35, 600, 0.8); tone(90, 0, 0.3, 'sine', 0.4, 40); break;
      case 'whistle': tone(1400, 0, 0.55, 'sine', 0.06, 500); break;
      case 'explosion': noise(0, 0.8, 380, 1); tone(60, 0, 0.6, 'sine', 0.45, 28); break;
      case 'capture': [523, 659, 784].forEach((f, i) => tone(f, i * 0.09, 0.18, 'triangle', 0.18)); break;
      case 'victory': [392, 523, 659, 784, 1046].forEach((f, i) => tone(f, i * 0.12, 0.5, 'triangle', 0.16)); break;
      case 'defeat': [392, 349, 311, 262].forEach((f, i) => tone(f, i * 0.18, 0.4, 'sine', 0.16)); break;
    }
  }
}

export const audio = new Audio();
