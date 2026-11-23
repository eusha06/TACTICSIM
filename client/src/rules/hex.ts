// Hex math mirrored from TacticSim.Sim/Hex.cs. Odd-r offset storage, cube math inside.
// Preview only: the server result is always what's shown after Execute.

export interface Hex {
  col: number;
  row: number;
}

const DIRECTIONS: ReadonlyArray<[number, number]> = [[1, 0], [1, -1], [0, -1], [-1, 0], [-1, 1], [0, 1]];

export const hex = (col: number, row: number): Hex => ({ col, row });
export const same = (a: Hex | null | undefined, b: Hex | null | undefined): boolean =>
  !!a && !!b && a.col === b.col && a.row === b.row;
export const label = (h: Hex): string => `${String.fromCharCode(65 + h.col)}${h.row + 1}`;
export const key = (h: Hex): string => `${h.col},${h.row}`;

export function toCube(h: Hex): [number, number, number] {
  const x = h.col - (h.row - (h.row & 1)) / 2;
  return [x, -x - h.row, h.row];
}

// `| 0` turns JavaScript's -0 into 0 so hexes compare equal to the C# vectors.
export const fromCube = (x: number, z: number): Hex => ({ col: (x + (z - (z & 1)) / 2) | 0, row: z | 0 });

export function distance(a: Hex, b: Hex): number {
  const [ax, ay, az] = toCube(a);
  const [bx, by, bz] = toCube(b);
  return Math.max(Math.abs(ax - bx), Math.abs(ay - by), Math.abs(az - bz));
}

export const adjacent = (a: Hex, b: Hex): boolean => distance(a, b) === 1;

export function neighbors(h: Hex): Hex[] {
  const [x, , z] = toCube(h);
  return DIRECTIONS.map(([dx, dz]) => fromCube(x + dx, z + dz));
}

/** Same arithmetic, step for step, as HexMath.Line in C#, so previews match the server. */
export function line(a: Hex, b: Hex): Hex[] {
  const n = distance(a, b);
  const [ax, ay, az] = toCube(a);
  const [bx, by, bz] = toCube(b);
  const x1 = ax + 1e-6, y1 = ay + 2e-6, z1 = az - 3e-6;
  const x2 = bx + 1e-6, y2 = by + 2e-6, z2 = bz - 3e-6;
  const result: Hex[] = [];
  for (let i = 0; i <= n; i++) {
    const t = n === 0 ? 0 : i / n;
    result.push(cubeRound(x1 + (x2 - x1) * t, y1 + (y2 - y1) * t, z1 + (z2 - z1) * t));
  }
  return result;
}

function cubeRound(x: number, y: number, z: number): Hex {
  let rx = Math.floor(x + 0.5), ry = Math.floor(y + 0.5), rz = Math.floor(z + 0.5);
  const dx = Math.abs(rx - x), dy = Math.abs(ry - y), dz = Math.abs(rz - z);
  if (dx > dy && dx > dz) rx = -ry - rz;
  else if (dy > dz) ry = -rx - rz;
  else rz = -rx - ry;
  return fromCube(rx, rz);
}
