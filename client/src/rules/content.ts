import type { Hex } from './hex';

// Shapes of /api/v2/rules (content/v1/*.json). Numbers are never hard-coded on the client.
export interface RulesDef {
  rulesVersion: string; ticksPerTurn: number; holdCover: number; roadMarchBonus: number;
  barrageTick: number; hillVisionBonus: number; minDamage: number;
}
export interface TerrainDef {
  code: string; id: string; name: string; footCost: number | null; vehicleCost: number | null;
  cover: number; blocksLos: boolean; road?: boolean; hill?: boolean;
}
export interface UnitDef {
  id: string; name: string; short: string; class: 'foot' | 'vehicle'; hp: number; move: number;
  minRange: number; maxRange: number; atkFoot: number; atkVehicle: number; vision: number;
  captures: boolean; indirect?: boolean; cannotMoveAndFire?: boolean; coverMods?: Record<string, number>; special: string;
}
export interface ObjectiveDef { id: string; label: string; col: number; row: number }
export interface MapDef {
  id: string; name: string; width: number; height: number; tiles: string[];
  objectives: ObjectiveDef[]; turnLimit: number; vpToWin: number;
}
export interface Content {
  rulesVersion: string; rules: RulesDef; terrain: TerrainDef[]; units: UnitDef[]; maps: MapDef[];
}

/** One map + the terrain table. Mirrors Board in Content.cs. */
export class Board {
  readonly width: number;
  readonly height: number;
  private readonly terrain: Map<string, TerrainDef>;

  constructor(readonly map: MapDef, readonly content: Content) {
    this.width = map.width;
    this.height = map.height;
    this.terrain = new Map(content.terrain.map((t) => [t.code, t]));
  }

  inBounds(h: Hex): boolean {
    return h.col >= 0 && h.col < this.width && h.row >= 0 && h.row < this.height;
  }
  terrainAt(h: Hex): TerrainDef {
    return this.terrain.get(this.map.tiles[h.row][h.col])!;
  }
  index(h: Hex): number {
    return h.row * this.width + h.col;
  }
  fromIndex(i: number): Hex {
    return { col: i % this.width, row: Math.floor(i / this.width) };
  }
  moveCost(h: Hex, unit: UnitDef): number | null {
    if (!this.inBounds(h)) return null;
    const t = this.terrainAt(h);
    return unit.class === 'foot' ? t.footCost : t.vehicleCost;
  }
  *all(): Generator<Hex> {
    for (let row = 0; row < this.height; row++) for (let col = 0; col < this.width; col++) yield { col, row };
  }
  objectiveAt(h: Hex): ObjectiveDef | undefined {
    return this.map.objectives.find((o) => o.col === h.col && o.row === h.row);
  }
  unit(type: string): UnitDef {
    return this.content.units.find((u) => u.id === type)!;
  }
}
