// Original "tabletop diorama" art, written as SVG. BootScene rasterizes every piece once into a
// Phaser texture. Nothing here is copied from anywhere; the shapes are fictional miniatures.

export const HEX_R = 44; // hex radius in world pixels (center to corner)
export const HEX_W = Math.sqrt(3) * HEX_R;
export const HEX_H = 2 * HEX_R;
export const ART_SCALE = 2; // textures are drawn at 2x and shown at 0.5x so zooming stays crisp

export const SIDE_COLOR = { blue: '#4b8fd6', red: '#e0813a' } as const;
const FIGURE = {
  blue: { light: '#8fb1d4', mid: '#5f84aa', dark: '#2f4a66' },
  red: { light: '#e6b07c', mid: '#c4804a', dark: '#6e4224' },
} as const;

function rng(seed: number) {
  return () => {
    seed = (seed + 0x6d2b79f5) | 0;
    let t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

const hexPoints = (cx: number, cy: number, r: number) =>
  Array.from({ length: 6 }, (_, i) => {
    const a = ((60 * i - 90) * Math.PI) / 180;
    return `${(cx + r * Math.cos(a)).toFixed(2)},${(cy + r * Math.sin(a)).toFixed(2)}`;
  }).join(' ');

const svg = (w: number, h: number, body: string) =>
  `<svg xmlns="http://www.w3.org/2000/svg" width="${w * ART_SCALE}" height="${h * ART_SCALE}" viewBox="0 0 ${w} ${h}">${body}</svg>`;

/** A hex tile with bevel light (top-left) and shade (bottom-right), clipped details inside. */
function tile(base: string, details: string, id: string): string {
  const cx = HEX_W / 2, cy = HEX_H / 2, r = HEX_R - 0.8;
  return svg(HEX_W, HEX_H, `
    <defs>
      <clipPath id="c${id}"><polygon points="${hexPoints(cx, cy, r)}"/></clipPath>
      <linearGradient id="g${id}" x1="0" y1="0" x2="1" y2="1">
        <stop offset="0" stop-color="#fff" stop-opacity=".16"/><stop offset=".5" stop-color="#fff" stop-opacity="0"/>
        <stop offset="1" stop-color="#000" stop-opacity=".22"/>
      </linearGradient>
    </defs>
    <polygon points="${hexPoints(cx, cy, r)}" fill="${base}"/>
    <g clip-path="url(#c${id})">${details}</g>
    <polygon points="${hexPoints(cx, cy, r)}" fill="url(#g${id})"/>
    <polygon points="${hexPoints(cx, cy, r - 0.6)}" fill="none" stroke="#1d241b" stroke-opacity=".35" stroke-width="1.2"/>`);
}

function tufts(rand: () => number, count: number, colors: string[]): string {
  let out = '';
  for (let i = 0; i < count; i++) {
    const x = 6 + rand() * (HEX_W - 12), y = 8 + rand() * (HEX_H - 16);
    const c = colors[Math.floor(rand() * colors.length)];
    out += `<path d="M${x} ${y}l-2 -4M${x} ${y}l0 -5M${x} ${y}l2 -4" stroke="${c}" stroke-width="1.1" stroke-linecap="round"/>`;
  }
  return out;
}

function blotches(rand: () => number, count: number, colors: string[], size: number): string {
  let out = '';
  for (let i = 0; i < count; i++) {
    const x = rand() * HEX_W, y = rand() * HEX_H, rx = size * (0.6 + rand()), ry = rx * (0.5 + rand() * 0.4);
    out += `<ellipse cx="${x}" cy="${y}" rx="${rx}" ry="${ry}" fill="${colors[i % colors.length]}" opacity=".55"/>`;
  }
  return out;
}

function house(x: number, y: number, w: number, h: number, roof: string): string {
  // Two roof planes: the lower one shaded, with a ridge line between them.
  return `<rect x="${x + 2}" y="${y + 3}" width="${w}" height="${h}" fill="#000" opacity=".28" rx="1"/>
    <rect x="${x - 1}" y="${y - 1}" width="${w + 2}" height="${h + 2}" fill="${roof}" rx="1"/>
    <rect x="${x - 1}" y="${y + h / 2}" width="${w + 2}" height="${h / 2 + 1}" fill="#000" opacity=".2" rx="1"/>
    <line x1="${x - 1}" y1="${y + h / 2}" x2="${x + w + 1}" y2="${y + h / 2}" stroke="#3b2a20" stroke-opacity=".55"/>`;
}

/** Terrain textures, 2–3 variants each so the map doesn't tile visibly. Keys: t-<id>-<n>. */
export function terrainArt(): Record<string, string> {
  const out: Record<string, string> = {};
  for (let v = 0; v < 3; v++) {
    const r = rng(101 + v * 17);
    const grassBase = ['#7f9254', '#859857', '#7a8d50'][v];
    out[`t-open-${v}`] = tile(grassBase, blotches(r, 5, ['#8fa262', '#718648'], 9) + tufts(r, 9, ['#62763e', '#9bad69']), `o${v}`);
    out[`t-road-${v}`] = tile(grassBase, blotches(r, 4, ['#8fa262', '#718648'], 8) + tufts(r, 6, ['#62763e']), `r${v}`);
    out[`t-forest-${v}`] = tile('#55703d', blotches(r, 7, ['#4a6435', '#61803f', '#3f5830'], 10) + tufts(r, 6, ['#3c5530']), `f${v}`);
    const hr = rng(301 + v * 13);
    const hx = HEX_W / 2 + (hr() - 0.5) * 8, hy = HEX_H / 2 + (hr() - 0.5) * 8;
    out[`t-hill-${v}`] = tile('#869656', `
      <ellipse cx="${hx + 4}" cy="${hy + 6}" rx="36" ry="30" fill="#5f6e3c" opacity=".5"/>
      <ellipse cx="${hx}" cy="${hy}" rx="34" ry="28" fill="#97a463"/>
      <ellipse cx="${hx - 2}" cy="${hy - 3}" rx="24" ry="19" fill="#a7b26f" stroke="#7d8a4c" stroke-opacity=".6"/>
      <ellipse cx="${hx - 4}" cy="${hy - 6}" rx="13" ry="10" fill="#b8c07e" stroke="#7d8a4c" stroke-opacity=".5"/>
      ${tufts(hr, 5, ['#6f7d44'])}`, `h${v}`);
    const tr = rng(501 + v * 7);
    const roofs = ['#9c5b3f', '#7f6a57', '#a8743f', '#8a4e3a'];
    let houses = '';
    const spots = [[14, 22], [40, 16], [22, 50], [48, 46], [32, 33]];
    spots.forEach(([x, y], i) => {
      if (i === 4 && v === 1) return;
      houses += house(x + (tr() - 0.5) * 4, y + (tr() - 0.5) * 4, 12 + tr() * 5, 10 + tr() * 4, roofs[(i + v) % roofs.length]);
    });
    out[`t-town-${v}`] = tile('#8a9159', `<rect x="0" y="0" width="${HEX_W}" height="${HEX_H}" fill="#9a9a6e" opacity=".45"/>${houses}`, `t${v}`);
    const wr = rng(701 + v * 5);
    let ripples = '';
    for (let i = 0; i < 6; i++) {
      const x = 8 + wr() * (HEX_W - 16), y = 10 + wr() * (HEX_H - 20);
      ripples += `<path d="M${x} ${y} q4 -3 8 0 t8 0" stroke="#8cb9c0" stroke-width="1.2" fill="none" opacity=".55"/>`;
    }
    out[`t-river-${v}`] = tile('#3d6b78', `<rect width="${HEX_W}" height="${HEX_H}" fill="#467785" opacity=".6"/>${ripples}`, `w${v}`);
    out[`t-bridge-${v}`] = out[`t-river-${v}`];
  }
  return out;
}

/** A single tree canopy for forest hexes (separate sprites so they can sway). */
export function treeArt(): Record<string, string> {
  const out: Record<string, string> = {};
  ['#3f5c2f', '#4b6b35', '#36512a'].forEach((c, i) => {
    out[`tree-${i}`] = svg(28, 28, `
      <ellipse cx="16" cy="18" rx="11" ry="8" fill="#000" opacity=".28"/>
      <circle cx="13" cy="12" r="10" fill="${c}"/>
      <circle cx="10" cy="9" r="5" fill="#6d8f45" opacity=".55"/>
      <circle cx="16" cy="14" r="5" fill="#2b4422" opacity=".35"/>`);
  });
  return out;
}

type SideKey = 'blue' | 'red';

/** Top-down miniature, facing up (north). Rotated in-game to face its movement. */
function figure(type: string, side: SideKey): string {
  const c = FIGURE[side];
  const soldier = (x: number, y: number, gun = true) => `
    <ellipse cx="${x}" cy="${y + 2}" rx="4.6" ry="3.6" fill="${c.dark}"/>
    <ellipse cx="${x}" cy="${y + 1}" rx="4" ry="3.2" fill="${c.mid}"/>
    <circle cx="${x}" cy="${y - 1}" r="2.4" fill="${c.light}" stroke="${c.dark}" stroke-width=".6"/>
    ${gun ? `<line x1="${x + 3}" y1="${y + 1}" x2="${x + 3}" y2="${y - 6}" stroke="#2a2a22" stroke-width="1.3" stroke-linecap="round"/>` : ''}`;
  const body: Record<string, string> = {
    rifle: soldier(32, 25) + soldier(24, 38) + soldier(40, 38),
    at: soldier(26, 33, false) + soldier(38, 35) +
      `<rect x="22.5" y="18" width="3.4" height="17" rx="1.2" fill="#3a3a30"/><rect x="21.8" y="16" width="4.8" height="4" rx="1" fill="#22221c"/>`,
    armor: `
      <rect x="19" y="16" width="26" height="34" rx="4" fill="${c.dark}"/>
      <rect x="16" y="15" width="6" height="36" rx="2" fill="#2b2b24"/><rect x="42" y="15" width="6" height="36" rx="2" fill="#2b2b24"/>
      <rect x="21" y="18" width="22" height="30" rx="3" fill="${c.mid}"/>
      <rect x="30.4" y="4" width="3.2" height="22" rx="1.2" fill="#2f2f27"/>
      <circle cx="32" cy="32" r="8.5" fill="${c.light}" stroke="${c.dark}" stroke-width="1.4"/>
      <circle cx="34" cy="34" r="2.2" fill="${c.dark}"/>`,
    artillery: `
      <path d="M32 34 L20 54 M32 34 L44 54" stroke="#3a3328" stroke-width="3.2" stroke-linecap="round"/>
      <circle cx="21" cy="34" r="6" fill="#2b2b24"/><circle cx="43" cy="34" r="6" fill="#2b2b24"/>
      <rect x="22" y="26" width="20" height="10" rx="2" fill="${c.mid}" stroke="${c.dark}" stroke-width="1.2"/>
      <rect x="30.2" y="2" width="3.6" height="30" rx="1.2" fill="#2f2f27"/>
      <rect x="29" y="2" width="6" height="4" rx="1" fill="#22221c"/>`,
    recon: `
      <rect x="18" y="20" width="6" height="9" rx="2" fill="#24241e"/><rect x="40" y="20" width="6" height="9" rx="2" fill="#24241e"/>
      <rect x="18" y="38" width="6" height="9" rx="2" fill="#24241e"/><rect x="40" y="38" width="6" height="9" rx="2" fill="#24241e"/>
      <path d="M22 18 L32 12 L42 18 L42 48 L22 48 Z" fill="${c.mid}" stroke="${c.dark}" stroke-width="1.4"/>
      <circle cx="32" cy="31" r="5.5" fill="${c.light}" stroke="${c.dark}" stroke-width="1.2"/>
      <rect x="31" y="16" width="2" height="12" fill="#2f2f27"/>`,
  };
  return svg(64, 64, `<g>${body[type]}</g>`);
}

/** Round base with a side-colored ring and a colorblind-safe shape marker (circle Blue, diamond Red). */
function base(side: SideKey): string {
  const ring = SIDE_COLOR[side];
  const marker = side === 'blue'
    ? `<circle cx="53" cy="13" r="6.5" fill="${ring}" stroke="#10161a" stroke-width="2"/>`
    : `<rect x="47" y="7" width="12" height="12" transform="rotate(45 53 13)" fill="${ring}" stroke="#10161a" stroke-width="2"/>`;
  return svg(64, 64, `
    <ellipse cx="34" cy="37" rx="27" ry="24" fill="#000" opacity=".35"/>
    <circle cx="32" cy="32" r="27" fill="#20261f"/>
    <circle cx="32" cy="32" r="24.5" fill="none" stroke="${ring}" stroke-width="5"/>
    <circle cx="32" cy="32" r="21.5" fill="#64774a"/>
    <circle cx="28" cy="27" r="13" fill="#7b8d58" opacity=".55"/>
    ${marker}`);
}

export function unitArt(): Record<string, string> {
  const out: Record<string, string> = {};
  for (const side of ['blue', 'red'] as const) {
    out[`base-${side}`] = base(side);
    for (const type of ['rifle', 'at', 'armor', 'artillery', 'recon']) out[`fig-${type}-${side}`] = figure(type, side);
  }
  out['ghost'] = svg(64, 64, `<circle cx="32" cy="32" r="24" fill="none" stroke="#f2e6d0" stroke-width="2.5" stroke-dasharray="5 4" opacity=".9"/>`);
  return out;
}

export function effectArt(): Record<string, string> {
  const s = HEX_W * 1.5, cx = s / 2, cy = s / 2;
  return {
    // Fog: soft-edged, desaturating veil. The blur is baked in so neighbors blend into one cloud.
    fog: svg(s, s, `<defs><filter id="b" x="-30%" y="-30%" width="160%" height="160%"><feGaussianBlur stdDeviation="7"/></filter></defs>
      <polygon points="${hexPoints(cx, cy, HEX_R * 1.08)}" fill="#1c2733" filter="url(#b)"/>`),
    scorch: svg(48, 48, `<defs><radialGradient id="s"><stop offset="0" stop-color="#14110d" stop-opacity=".85"/>
      <stop offset=".6" stop-color="#2a2218" stop-opacity=".45"/><stop offset="1" stop-color="#2a2218" stop-opacity="0"/></radialGradient></defs>
      <ellipse cx="24" cy="24" rx="23" ry="19" fill="url(#s)"/>`),
    wreck: svg(64, 64, `<ellipse cx="32" cy="36" rx="24" ry="18" fill="#0d0d0b" opacity=".45"/>
      <path d="M18 26 L42 20 L48 40 L24 46 Z" fill="#3b3a35"/><path d="M24 30 L38 27 L41 37 L27 40 Z" fill="#22211d"/>
      <path d="M33 22 L46 10" stroke="#262520" stroke-width="3" stroke-linecap="round"/>`),
    spark: svg(16, 16, `<defs><radialGradient id="p"><stop offset="0" stop-color="#fff"/><stop offset="1" stop-color="#fff" stop-opacity="0"/></radialGradient></defs><circle cx="8" cy="8" r="8" fill="url(#p)"/>`),
    smoke: svg(32, 32, `<defs><radialGradient id="m"><stop offset="0" stop-color="#d8d4cc" stop-opacity=".9"/><stop offset="1" stop-color="#d8d4cc" stop-opacity="0"/></radialGradient></defs><circle cx="16" cy="16" r="16" fill="url(#m)"/>`),
    shell: svg(12, 12, `<circle cx="6" cy="6" r="3.4" fill="#2a2620" stroke="#f5d68a" stroke-width="1"/>`),
    ...flags(),
  };
}

function flags(): Record<string, string> {
  const flag = (color: string) => svg(40, 52, `
    <ellipse cx="9" cy="48" rx="7" ry="3" fill="#000" opacity=".35"/>
    <rect x="7.5" y="6" width="2.6" height="42" rx="1" fill="#e9e2cf"/>
    <path d="M10 7 C18 4 24 11 34 8 L34 24 C24 27 18 20 10 23 Z" fill="${color}" stroke="#151a16" stroke-width="1.2"/>`);
  return { 'flag-neutral': flag('#d9d4c3'), 'flag-blue': flag(SIDE_COLOR.blue), 'flag-red': flag(SIDE_COLOR.red) };
}

/** Order icons: one path set, used both as canvas textures and inline in the DOM HUD. */
export const ICONS: Record<string, string> = {
  move: '<path d="M4 12h12M11 6l6 6-6 6" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"/>',
  attack: '<circle cx="12" cy="12" r="7" fill="none" stroke="currentColor" stroke-width="2.2"/><path d="M12 2v6M12 16v6M2 12h6M16 12h6" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"/>',
  hold: '<path d="M12 3l7 3v5c0 5-3 8-7 10-4-2-7-5-7-10V6z" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linejoin="round"/>',
  overwatch: '<path d="M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12z" fill="none" stroke="currentColor" stroke-width="2.2"/><circle cx="12" cy="12" r="3" fill="currentColor"/>',
  barrage: '<path d="M12 2l2 6 6-3-3 6 5 2-6 2 3 6-6-3-1 6-2-6-6 3 3-6-5-2 6-2-3-6 6 3z" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"/>',
  eye: '<path d="M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12z" fill="none" stroke="currentColor" stroke-width="2"/><circle cx="12" cy="12" r="3" fill="currentColor"/>',
  flag: '<path d="M5 21V4M5 4c5-2 8 2 14 0v9c-6 2-9-2-14 0" fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="round"/>',
  trophy: '<path d="M7 4h10v4a5 5 0 0 1-10 0zM5 5H3v2a3 3 0 0 0 4 3M19 5h2v2a3 3 0 0 1-4 3M12 13v4M8 21h8M9 17h6" fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="round"/>',
  clock: '<circle cx="12" cy="12" r="9" fill="none" stroke="currentColor" stroke-width="2"/><path d="M12 7v5l3 2" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"/>',
  terrain: '<path d="M2 19l6-9 4 5 3-4 7 8z" fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="round"/>',
  damage: '<path d="M13 2L4 14h7l-1 8 9-12h-7z" fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="round"/>',
  units: '<circle cx="8" cy="9" r="3" fill="none" stroke="currentColor" stroke-width="2"/><circle cx="16" cy="9" r="3" fill="none" stroke="currentColor" stroke-width="2"/><path d="M3 20c0-3 2-5 5-5s5 2 5 5M11 20c0-3 2-5 5-5s5 2 5 5" fill="none" stroke="currentColor" stroke-width="2"/>',
};

export const icon = (name: string, size = 18) =>
  `<svg class="icon" viewBox="0 0 24 24" width="${size}" height="${size}" aria-hidden="true">${ICONS[name]}</svg>`;

export function iconArt(): Record<string, string> {
  const out: Record<string, string> = {};
  for (const name of ['move', 'attack', 'hold', 'overwatch', 'barrage'])
    out[`icon-${name}`] = svg(24, 24, `<circle cx="12" cy="12" r="11.5" fill="#141b17" stroke="#f2e6c8" stroke-width="1.2"/>
      <g transform="translate(4.8 4.8) scale(.6)" color="#f2e6c8">${ICONS[name].replace(/currentColor/g, '#f2e6c8')}</g>`);
  return out;
}

/** Every texture, ready for BootScene. */
export const allArt = (): Record<string, string> => ({ ...terrainArt(), ...treeArt(), ...unitArt(), ...effectArt(), ...iconArt() });
