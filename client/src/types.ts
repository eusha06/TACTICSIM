import type { Hex } from './rules/hex';

// JSON shapes from /api/v2 (camelCase, string enums). See TacticSim.Sim Fog.cs and MatchV2Service.cs.
export type Side = 'blue' | 'red';
export type OrderType = 'hold' | 'move' | 'attack' | 'overwatch' | 'barrage';

export interface Order {
  unitId: number;
  type: OrderType;
  path?: Hex[];
  targetId?: number;
  targetHex?: Hex;
}

export interface UnitView { id: number; side: Side; type: string; pos: Hex; hp: number; maxHp: number }
export interface LastSeen { unitId: number; type: string; pos: Hex; hp: number; turn: number }
export interface VpPoint { turn: number; blue: number; red: number }

export interface MatchView {
  scenario: string; rulesVersion: string; viewer: Side; turn: number; turnLimit: number; vpToWin: number;
  vpBlue: number; vpRed: number; result?: 'blueWins' | 'redWins' | 'draw'; endReason?: string;
  units: UnitView[]; lastSeen: LastSeen[]; control: Record<string, Side | null>; visible: number[];
  vpHistory: VpPoint[]; kills: number; losses: number; fogLifted: boolean;
}

export interface SimEvent {
  kind: string; unitId?: number; targetId?: number; from?: Hex; to?: Hex; damage: number; hp: number;
  destroyed: boolean; side?: Side; objective?: string; reason?: string; ticks: number; amount: number;
}

export interface UnitFrame { id: number; side: Side; type: string; pos: Hex; hp: number }
export interface TickView { tick: number; units: UnitFrame[]; events: SimEvent[]; visible: number[] }
export interface ReplayView { turn: number; ticks: TickView[]; endEvents: SimEvent[]; debrief: string[] }

export interface MatchSummary {
  id: string; scenario: string; difficulty: string; rulesVersion: string; turn: number;
  status: 'active' | 'blueWins' | 'redWins' | 'draw'; vpBlue: number; vpRed: number; version: number;
  createdAt: string; updatedAt: string; completedAt?: string;
}

export interface MatchPayload { match: MatchSummary; state: MatchView; replay?: ReplayView; error?: string; unitId?: number }
