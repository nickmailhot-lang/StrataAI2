import { RetentionSourceClassificationIntent, parseRetentionSourceReview, readRetentionSourceReview, type RetentionSourceReview } from './retentionSourceClient';
import { completeRetentionSourceClassification, forgetOrganizationRetentionSourceClassifications,
  restoreRetentionSourceClassification, retainRetentionSourceClassification } from './retentionSourceRecovery';

const organization = '11111111-1111-4111-8111-111111111111', actor = '22222222-2222-4222-8222-222222222222';
const card = '33333333-3333-4333-8333-333333333333', board = '44444444-4444-4444-8444-444444444444';
const list = '55555555-5555-4555-8555-555555555555', key = '66666666-6666-4666-8666-666666666666';
const other = '77777777-7777-4777-8777-777777777777';
const review: RetentionSourceReview = { organizationId: organization, recordId: card, kind: 'card', parentCardId: null,
  cardId: card, boardId: board, listId: list, cardVersion: 4, listVersion: 2, sourceVersion: 4,
  cardState: 'active', listState: 'archived', sourceState: 'active', createdAt: '2026-10-11T12:00:00.000001Z' };
const receipt = { reference: { organizationId: organization, recordClass: 'operationalWork', recordId: card, kind: 'card',
  parentCardId: null, contextVersion: 1, originalBoardId: board, sourceVersion: 4 }, category: 'maintenance', jurisdiction: null,
  actorId: actor, eventId: other, registeredAt: '2026-10-11T12:00:00.000002Z' };
const original = () => RetentionSourceClassificationIntent.review(actor, review, 'maintenance', null, true, key);
const signal = () => new AbortController().signal;
const storageKey = `strataai:retention-source-classification:v1:${actor}:${organization}`;
function transport(post: (options: RequestInit) => Response | Promise<Response>, currentActor = actor) {
  const fetch = vi.fn(async (url: string, options: RequestInit = {}) => options.method === 'POST' ? post(options)
    : new Response(JSON.stringify(url === '/me'
      ? { id: currentActor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'UTC' } : review)));
  vi.stubGlobal('fetch', fetch); return fetch;
}
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); sessionStorage.clear(); vi.useRealTimers(); });
describe('PRD-70 exact classification review and original receipt recovery', () => {
  it('replays identical retained bytes after a lost response without a fresh source review after movement', async () => {
    const mutable = { ...review }, intent = RetentionSourceClassificationIntent.review(actor, mutable, 'maintenance', null, true, key);
    retainRetentionSourceClassification(sessionStorage, intent); mutable.boardId = other; mutable.cardVersion = 99;
    const bodies: string[] = []; let count = 0;
    const fetch = transport(options => {
      bodies.push(options.body as string);
      expect(new Headers(options.headers).get('Idempotency-Key')).toBe(key);
      expect(new Headers(options.headers).get('X-StrataAI-Expected-Actor')).toBe(actor);
      expect(new Headers(options.headers).get('X-StrataAI-Request')).toBe('1');
      if (++count === 1) throw new TypeError('Lost response after commit'); return new Response(JSON.stringify(receipt));
    });
    await expect(intent.submit(signal())).rejects.toThrow();
    const restored = restoreRetentionSourceClassification(sessionStorage, actor, organization)!;
    restored.reviewed().review.boardId = other;
    await expect(restored.submit(signal())).resolves.toEqual(receipt);
    expect(bodies).toEqual([bodies[0], bodies[0]]); expect(JSON.parse(bodies[0]).review).toEqual(review);
    expect(fetch.mock.calls.filter(([url]) => url.endsWith('/review'))).toHaveLength(0);
    completeRetentionSourceClassification(sessionStorage, restored); expect(sessionStorage.getItem(storageKey)).toBeNull();
  });
  it('requires human confirmation and exact, relationship-consistent source evidence', () => {
    expect(() => RetentionSourceClassificationIntent.review(actor, review, 'maintenance', null, false, key)).toThrow();
    for (const bad of [{ ...review, title: 'Protected title' }, { ...review, sourceVersion: 3 }, { ...review, parentCardId: card },
      { ...review, listVersion: Number.MAX_SAFE_INTEGER + 1 }, { ...review, createdAt: '2026-02-30T00:00:00Z' },
      { ...review, cardState: 'deleted' }, { ...review, kind: 'auditEvent' }])
      expect(() => parseRetentionSourceReview(bad, organization, 'card', card)).toThrow();
    const comment = { ...review, kind: 'cardComment', recordId: other, parentCardId: card, sourceVersion: 1 };
    expect(parseRetentionSourceReview(comment, organization, 'cardComment', other)).toEqual(comment);
    expect(() => parseRetentionSourceReview({ ...comment, parentCardId: null }, organization, 'cardComment', other)).toThrow();
  });
  it('refuses corrupted or substituted acknowledgments while preserving the original for recovery', async () => {
    const intent = original(); retainRetentionSourceClassification(sessionStorage, intent);
    for (const bad of [{ ...receipt, actorId: other }, { ...receipt, category: 'later' }, { ...receipt, jurisdiction: 'BC' },
      { ...receipt, registeredAt: review.createdAt.replace('000001', '000000') }, { ...receipt, protectedBody: 'private' },
      { ...receipt, reference: { ...receipt.reference, originalBoardId: other } },
      { ...receipt, reference: { ...receipt.reference, contextVersion: 0 } },
      { ...receipt, reference: { ...receipt.reference, recordClass: 'document' } }]) {
      transport(() => new Response(JSON.stringify(bad)));
      await expect(intent.submit(signal())).rejects.toMatchObject({ status: 503 });
      expect(restoreRetentionSourceClassification(sessionStorage, actor, organization)?.key).toBe(key);
    }
  });
  it('sends no mutation when the authenticated actor changed', async () => {
    const fetch = transport(() => new Response(JSON.stringify(receipt)), other), sent = vi.fn();
    await expect(original().submit(signal(), sent)).rejects.toMatchObject({ status: 401 });
    expect(sent).not.toHaveBeenCalled(); expect(fetch.mock.calls.filter(([, options]) => options?.method === 'POST')).toHaveLength(0);
  });
  it('does not acknowledge a valid result after the current actor changes during delivery', async () => {
    const intent = original(); retainRetentionSourceClassification(sessionStorage, intent); let changed = false;
    vi.stubGlobal('fetch', vi.fn(async (_url: string, options: RequestInit = {}) => {
      if (options.method === 'POST') { changed = true; return new Response(JSON.stringify(receipt)); }
      return new Response(JSON.stringify({ id: changed ? other : actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'UTC' }));
    }));
    await expect(intent.submit(signal())).rejects.toMatchObject({ status: 401 });
    expect(restoreRetentionSourceClassification(sessionStorage, actor, organization)?.key).toBe(key);
  });
  it('bounds a stalled submission and preserves the retained original', async () => {
    vi.useFakeTimers(); const intent = original(); retainRetentionSourceClassification(sessionStorage, intent);
    transport(() => new Promise<Response>(() => {})); const sent = vi.fn();
    const pending = expect(intent.submit(signal(), sent)).rejects.toMatchObject({ status: 503 });
    await vi.advanceTimersByTimeAsync(15_000); await pending;
    expect(sent).toHaveBeenCalledTimes(1); expect(restoreRetentionSourceClassification(sessionStorage, actor, organization)?.key).toBe(key);
  });
  it('refuses a second source or altered key until the original is recovered', () => {
    const intent = original(); retainRetentionSourceClassification(sessionStorage, intent);
    const replacement = RetentionSourceClassificationIntent.review(actor, { ...review, recordId: other, cardId: other }, 'maintenance', null, true);
    expect(() => retainRetentionSourceClassification(sessionStorage, replacement)).toThrow();
    expect(() => completeRetentionSourceClassification(sessionStorage, replacement)).toThrow();
    expect(restoreRetentionSourceClassification(sessionStorage, actor, organization)?.key).toBe(key);
    const corrupt = intent.recoveryRecord(); corrupt.body = corrupt.body.replace('"confirmed":true', '"confirmed":false');
    expect(() => RetentionSourceClassificationIntent.restore(corrupt)).toThrow();
    sessionStorage.setItem(storageKey, JSON.stringify({ ...intent.recoveryRecord(), actorId: other }));
    expect(() => restoreRetentionSourceClassification(sessionStorage, actor, organization)).toThrow();
  });
  it('clears only the definitively withdrawn Organization without touching unrelated recovery', () => {
    retainRetentionSourceClassification(sessionStorage, original());
    const elsewhere = RetentionSourceClassificationIntent.review(actor, { ...review, organizationId: other }, 'maintenance', null, true);
    retainRetentionSourceClassification(sessionStorage, elsewhere); sessionStorage.setItem('unrelated', 'retained');
    forgetOrganizationRetentionSourceClassifications(organization);
    expect(restoreRetentionSourceClassification(sessionStorage, actor, organization)).toBeUndefined();
    expect(restoreRetentionSourceClassification(sessionStorage, actor, other)?.key).toBe(elsewhere.key);
    expect(sessionStorage.getItem('unrelated')).toBe('retained');
  });
  it('checks source context and authenticated actor around a current review', async () => {
    const fetch = transport(() => new Response('unexpected'));
    await expect(readRetentionSourceReview(organization, 'card', card, signal(), actor)).resolves.toEqual({ actorId: actor, review });
    expect(fetch.mock.calls.map(([url]) => url)).toEqual(['/me', `/organizations/${organization}/retention-sources/card/${card}/review`, '/me']);
    expect(() => readRetentionSourceReview(organization, 'auditEvent' as 'card', card, signal())).toThrow();
  });
});
