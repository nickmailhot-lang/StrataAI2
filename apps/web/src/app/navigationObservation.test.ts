import { afterEach, expect, it, vi } from 'vitest';
import { ChangedNavigationActor, NavigationAcknowledgments } from './navigationInteraction';
import { createNavigationIntent, submitNavigationIntent } from './navigationObservation';
const actor = '11111111-1111-4111-8111-111111111111';
const organization = '22222222-2222-4222-8222-222222222222';
const board = '33333333-3333-4333-8333-333333333333';
const event = '44444444-4444-4444-8444-444444444444';
const now = Date.parse('2026-10-05T12:00:00Z');
const target = { kind: 'board' as const, organization, board, version: 3 };
const source = () => ({ eventId: event, actorId: actor, organizationId: organization, boardId: board,
  eventType: 'BOARD_OPENED', entityType: 'Board', entityId: board, version: 3, metadata: {}, createdAt: '2026-10-05T12:00:00Z' });
const response = (value: unknown) => new Response(JSON.stringify(value), { status: 200 });
afterEach(() => vi.unstubAllGlobals());

it('PRD-01 retries the immutable account and target with the same key after a lost reply', async () => {
  const input = { ...target }; const intent = createNavigationIntent(actor, input, now); input.version = 4;
  expect(Object.isFrozen(intent)).toBe(true); expect(Object.isFrozen(intent.target)).toBe(true);
  let writes = 0;
  const fetch = vi.fn(async (path: string) => {
    if (path === '/me') return response({ id: actor });
    if (++writes === 1) throw new TypeError('Lost response');
    return response(source());
  }); vi.stubGlobal('fetch', fetch);
  const consumer = new NavigationAcknowledgments(), signal = new AbortController().signal;
  await expect(submitNavigationIntent(intent, signal, consumer, now)).rejects.toThrow();
  expect((await submitNavigationIntent(intent, signal, consumer, now + 1)).firstAcknowledgment).toBe(true);
  expect((await submitNavigationIntent(intent, signal, consumer, now + 2)).firstAcknowledgment).toBe(false);
  const requests = fetch.mock.calls.filter(([path]) => path !== '/me'); expect(requests).toHaveLength(3);
  for (const [path, options] of requests as unknown as [string, RequestInit][]) {
    expect(path).toBe(`/navigation/observations?kind=board&organizationId=${organization}&boardId=${board}&version=3`);
    expect(options.method).toBe('POST'); expect(options.body).toBeUndefined();
    const headers = new Headers(options.headers);
    expect(headers.get('Idempotency-Key')).toBe(intent.key); expect(headers.get('X-StrataAI-Expected-Actor')).toBe(actor);
    expect(headers.get('X-StrataAI-Request')).toBe('1');
  }
});
it.each(['before', 'after'])('PRD-01 refuses an account replacement %s dispatch', async when => {
  let reads = 0, writes = 0;
  vi.stubGlobal('fetch', vi.fn(async (path: string) => {
    if (path === '/me') return response({ id: ++reads === 1 && when === 'after' ? actor : event });
    writes++; return response(source());
  }));
  const consumer = new NavigationAcknowledgments(), consume = vi.spyOn(consumer, 'consume');
  await expect(submitNavigationIntent(createNavigationIntent(actor, target, now), new AbortController().signal, consumer, now))
    .rejects.toThrow(ChangedNavigationActor);
  expect(writes).toBe(when === 'before' ? 0 : 1); expect(consume).not.toHaveBeenCalled();
});
it('PRD-01 rejects expired intent before dispatch and a foreign response before consumption', async () => {
  const fetch = vi.fn(async (path: string) => response(path === '/me' ? { id: actor } : { ...source(), boardId: event }));
  vi.stubGlobal('fetch', fetch);
  const intent = createNavigationIntent(actor, target, now), consumer = new NavigationAcknowledgments(), consume = vi.spyOn(consumer, 'consume');
  await expect(submitNavigationIntent(intent, new AbortController().signal, consumer, now + 86400000)).rejects.toThrow('expired');
  expect(fetch).not.toHaveBeenCalled();
  await expect(submitNavigationIntent(intent, new AbortController().signal, consumer, now)).rejects.toThrow();
  expect(consume).not.toHaveBeenCalled();
});
it('PRD-01 withholds an acknowledgment when the initiating screen cancels', async () => {
  const controller = new AbortController(), consumer = new NavigationAcknowledgments(), consume = vi.spyOn(consumer, 'consume');
  vi.stubGlobal('fetch', vi.fn(async (path: string) => {
    if (path === '/me') return response({ id: actor });
    controller.abort(); return response(source());
  }));
  await expect(submitNavigationIntent(createNavigationIntent(actor, target, now), controller.signal, consumer, now)).rejects.toThrow();
  expect(consume).not.toHaveBeenCalled();
});
