import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { createBoardFilterChange, discardBoardFilterChange, restoreBoardFilterChange, retainBoardFilterChange, submitBoardFilterChange } from './boardFilterChange';
import { ChangedSearchInteractionActor, SearchInteractionAcknowledgments } from './searchInteraction';
const actor = '11111111-1111-4111-8111-111111111111', organization = '22222222-2222-4222-8222-222222222222';
const board = '33333333-3333-4333-8333-333333333333', event = '44444444-4444-4444-8444-444444444444';
const scope = { actor, organization, board }, now = Date.parse('2026-10-05T12:00:00Z');
const criteria = () => ({ keyword: ' Roof ', labels: [], members: [], match: 'all' as const });
const source = () => ({ eventId: event, eventType: 'BOARD_FILTER_CHANGED', actorId: actor, organizationId: organization, boardId: board,
  entityType: 'BoardFilter', entityId: event, version: 1, metadata: {}, createdAt: '2026-10-05T12:00:00.123456Z' });
const response = (value: unknown) => new Response(JSON.stringify(value), { status: 200 });
beforeEach(() => sessionStorage.clear()); afterEach(() => vi.unstubAllGlobals());
it('stores one immutable original under its admitted account and Board, and restores it after navigation', () => {
  const input = criteria(), intent = createBoardFilterChange(scope, 'apply', input, now);
  retainBoardFilterChange(sessionStorage, intent, now); input.keyword = 'Changed';
  const restored = restoreBoardFilterChange(sessionStorage, scope, now + 1);
  expect(restored).toEqual(intent); expect(new URLSearchParams(restored!.query).get('keyword')).toBe('Roof');
  expect(restoreBoardFilterChange(sessionStorage, { ...scope, actor: event }, now + 1)).toBeUndefined();
  expect(restoreBoardFilterChange(sessionStorage, { ...scope, board: event }, now + 1)).toBeUndefined();
  expect(Object.isFrozen(restored)).toBe(true);
  expect(() => retainBoardFilterChange(sessionStorage, createBoardFilterChange(scope, 'apply', { ...criteria(), keyword: 'Replacement' }, now), now + 1)).toThrow('original');
  expect(restoreBoardFilterChange(sessionStorage, scope, now + 2)).toEqual(intent);
});
it('discards expired or tampered originals without generating replacement keys', () => {
  const intent = createBoardFilterChange(scope, 'apply', criteria(), now); retainBoardFilterChange(sessionStorage, intent, now);
  expect(restoreBoardFilterChange(sessionStorage, scope, now + 86400000)).toBeUndefined(); expect(sessionStorage.length).toBe(0);
  sessionStorage.setItem(`strataai:board-filter-change:v1:${actor}:${organization}:${board}`, JSON.stringify({ ...intent, query: intent.query + '&actorId=' + event }));
  expect(restoreBoardFilterChange(sessionStorage, scope, now + 1)).toBeUndefined(); expect(sessionStorage.length).toBe(0);
  expect(() => createBoardFilterChange({ ...scope, actor: 'anonymous' }, 'apply', criteria(), now)).toThrow();
  expect(() => createBoardFilterChange(scope, 'clear', criteria(), now)).toThrow();
  expect(() => createBoardFilterChange(scope, 'clear', { ...criteria(), keyword: 'all' }, now)).toThrow();
});
it('retries the exact request after a lost response and consumes only the admitted canonical original', async () => {
  const intent = createBoardFilterChange(scope, 'apply', criteria(), now); retainBoardFilterChange(sessionStorage, intent, now);
  let writes = 0;
  const fetch = vi.fn(async (path: string) => {
    if (path === '/me') return response({ id: actor });
    if (++writes === 1) throw new TypeError('Lost response'); return response(source());
  }); vi.stubGlobal('fetch', fetch);
  const consumer = new SearchInteractionAcknowledgments(); const signal = new AbortController().signal;
  await expect(submitBoardFilterChange(intent, signal, consumer, now)).rejects.toThrow();
  const restored = restoreBoardFilterChange(sessionStorage, scope, now + 1)!;
  expect((await submitBoardFilterChange(restored, signal, consumer, now + 1)).firstAcknowledgment).toBe(true);
  expect((await submitBoardFilterChange(restored, signal, consumer, now + 2)).firstAcknowledgment).toBe(false);
  const requests = fetch.mock.calls.filter(([path]) => path !== '/me'); expect(requests).toHaveLength(3);
  for (const [path, options] of requests as unknown as [string, RequestInit][]) {
    expect(path).toBe(`/boards/${board}/cards/filter-change?${intent.query}`); expect(options.method).toBe('POST'); expect(options.body).toBeUndefined();
    const headers = new Headers(options.headers); expect(headers.get('Idempotency-Key')).toBe(intent.key);
    expect(headers.get('X-StrataAI-Expected-Actor')).toBe(actor); expect(headers.get('X-StrataAI-Request')).toBe('1');
  }
  discardBoardFilterChange(sessionStorage, scope); expect(sessionStorage.length).toBe(0);
});
it.each(['before', 'after'])('refuses a changed account %s dispatch without consuming an acknowledgment', async when => {
  let reads = 0, writes = 0;
  vi.stubGlobal('fetch', vi.fn(async (path: string) => {
    if (path === '/me') return response({ id: ++reads === 1 && when === 'after' ? actor : event });
    writes++; return response(source());
  }));
  const consumer = new SearchInteractionAcknowledgments(), consume = vi.spyOn(consumer, 'consume');
  await expect(submitBoardFilterChange(createBoardFilterChange(scope, 'apply', criteria(), now), new AbortController().signal, consumer, now))
    .rejects.toThrow(ChangedSearchInteractionActor);
  expect(writes).toBe(when === 'before' ? 0 : 1); expect(consume).not.toHaveBeenCalled();
});
it('withholds malformed or foreign-scope acknowledgments even after successful session checks', async () => {
  vi.stubGlobal('fetch', vi.fn(async (path: string) => response(path === '/me' ? { id: actor } : { ...source(), boardId: event })));
  const consumer = new SearchInteractionAcknowledgments(), consume = vi.spyOn(consumer, 'consume');
  await expect(submitBoardFilterChange(createBoardFilterChange(scope, 'apply', criteria(), now), new AbortController().signal, consumer, now)).rejects.toThrow();
  expect(consume).not.toHaveBeenCalled();
});
it('rejects expired dispatch before any account or protected request', async () => {
  const fetch = vi.fn(); vi.stubGlobal('fetch', fetch);
  await expect(submitBoardFilterChange(createBoardFilterChange(scope, 'apply', criteria(), now), new AbortController().signal,
    new SearchInteractionAcknowledgments(), now + 86400000)).rejects.toThrow('expired'); expect(fetch).not.toHaveBeenCalled();
});
it('does not consume a committed response after the initiating view cancels', async () => {
  const controller = new AbortController(), consumer = new SearchInteractionAcknowledgments(), consume = vi.spyOn(consumer, 'consume');
  vi.stubGlobal('fetch', vi.fn(async (path: string) => {
    if (path !== '/me') { controller.abort(); return response(source()); } return response({ id: actor });
  }));
  await expect(submitBoardFilterChange(createBoardFilterChange(scope, 'apply', criteria(), now), controller.signal, consumer, now)).rejects.toThrow();
  expect(consume).not.toHaveBeenCalled();
});
it('bounds retained recovery originals without evicting live requests', () => {
  for (let index = 0; index < 1000; index++) sessionStorage.setItem(`strataai:board-filter-change:v1:existing:${index}`, 'retained');
  expect(() => retainBoardFilterChange(sessionStorage, createBoardFilterChange(scope, 'apply', criteria(), now), now)).toThrow('full');
  expect(sessionStorage.length).toBe(1000);
});

it('reclaims only canonical expired originals of the current account at the storage limit', () => {
  const expired = createBoardFilterChange({ ...scope, board: event }, 'apply', criteria(), now - 86400000);
  const otherAccount = createBoardFilterChange({ ...scope, actor: event }, 'apply', criteria(), now - 86400000);
  const live = createBoardFilterChange({ ...scope, organization: event }, 'apply', criteria(), now - 1);
  const key = (value: typeof expired) => `strataai:board-filter-change:v1:${value.actor}:${value.organization}:${value.board}`;
  for (let index = 0; index < 997; index++) sessionStorage.setItem(`strataai:board-filter-change:v1:existing:${index}`, 'retained');
  for (const value of [expired, otherAccount, live]) sessionStorage.setItem(key(value), JSON.stringify(value));
  const fresh = createBoardFilterChange(scope, 'apply', criteria(), now);
  retainBoardFilterChange(sessionStorage, fresh, now);
  expect(sessionStorage.length).toBe(1000); expect(sessionStorage.getItem(key(expired))).toBeNull();
  expect(sessionStorage.getItem(key(otherAccount))).toBe(JSON.stringify(otherAccount));
  expect(restoreBoardFilterChange(sessionStorage, { ...scope, organization: event }, now)).toEqual(live);
  expect(restoreBoardFilterChange(sessionStorage, scope, now)).toEqual(fresh);
});
