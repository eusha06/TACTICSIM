import type { Content } from './rules/content';
import type { MatchPayload, MatchSummary, Order } from './types';

/** Thrown for any non-2xx reply. `message` is the server's plain-English reason, shown verbatim. */
export class ApiError extends Error {
  constructor(message: string, readonly status: number, readonly payload?: MatchPayload) {
    super(message);
  }
}

let token = '';

async function call<T>(method: 'GET' | 'POST', path: string, body?: unknown): Promise<T> {
  let response: Response;
  try {
    response = await fetch(`/api/v2${path}`, {
      method,
      credentials: 'same-origin',
      headers: method === 'POST' ? { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token } : {},
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch {
    throw new ApiError('Can’t reach the battlefield server. Check your connection and retry.', 0);
  }
  const data = (await response.json().catch(() => ({}))) as T & { error?: string };
  if (!response.ok) throw new ApiError(data.error ?? `Request failed (${response.status}).`, response.status, data as unknown as MatchPayload);
  return data;
}

/** Starts the session (owner cookie + antiforgery token). `onSlow` fires if the free host is still waking. */
export async function connect(onSlow: () => void): Promise<Content> {
  const slow = window.setTimeout(onSlow, 2000);
  try {
    token = (await call<{ token: string }>('GET', '/session')).token;
    return await call<Content>('GET', '/rules');
  } finally {
    window.clearTimeout(slow);
  }
}

export const listMatches = () => call<{ matches: MatchSummary[]; classic: unknown[] }>('GET', '/matches');
export const createMatch = (scenario: string, difficulty: string) => call<MatchPayload>('POST', '/matches', { scenario, difficulty });
export const getMatch = (id: string) => call<MatchPayload>('GET', `/matches/${id}`);
export const getTurn = (id: string, turn: number) => call<MatchPayload>('GET', `/matches/${id}/turns/${turn}`);
export const submitTurn = (id: string, expectedVersion: number, orders: Order[], commandId: string) =>
  call<MatchPayload>('POST', `/matches/${id}/turns`, { commandId, expectedVersion, orders });
