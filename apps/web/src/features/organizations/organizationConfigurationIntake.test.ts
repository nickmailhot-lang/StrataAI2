import { afterEach, describe, expect, it, vi } from 'vitest';
import { readConfigurationIntakeLists } from './organizationConfigurationClient';

const organizationId = '11111111-1111-4111-8111-111111111111';
const actorId = '22222222-2222-4222-8222-222222222222';
const boardId = '33333333-3333-4333-8333-333333333333';
const profile = { id: actorId, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'UTC' };
const current = { organizationId, version: 0, revision: null };
const rank = (index: number) => String(index).padStart(30, '0');
const row = (index: number) => ({ id: `44444444-4444-4444-8444-${String(index).padStart(12, '0')}`, name: `List ${index}`, version: 1, rank: rank(index) });
function page(count = 1) { return { organizationId, board: { id: boardId, name: 'Owning Board', version: 1 }, items: Array.from({ length: count }, (_, i) => row(i + 1)), nextAfterRank: count === 50 ? rank(50) : null }; }
function sequence(...values: unknown[]) {
  const fetch = vi.fn(); values.forEach(value => fetch.mockResolvedValueOnce(new Response(JSON.stringify(value), { status: 200 })));
  vi.stubGlobal('fetch', fetch); return fetch;
}
afterEach(() => vi.unstubAllGlobals());
describe('PRD-27 bounded private intake List discovery', () => {
  it('reads only the scoped List route and returns a bounded continuation with account and configuration fences', async () => {
    const fetch = sequence(profile, current, page(50), current, profile);
    const result = await readConfigurationIntakeLists(organizationId, actorId, boardId, new AbortController().signal);
    expect(result.lists).toHaveLength(50); expect(result.nextAfterRank).toBe(rank(50));
    expect(fetch.mock.calls.map(call => call[0])).toEqual(['/me', `/organizations/${organizationId}/configuration`,
      `/organizations/${organizationId}/configuration/intake-boards/${boardId}/lists`, `/organizations/${organizationId}/configuration`, '/me']);
    expect(new Headers(fetch.mock.calls[2][1].headers).get('X-StrataAI-Expected-Actor')).toBe(actorId);
    expect(result.lists[0]).toEqual({ id: row(1).id, name: 'List 1' });
  });
  it('passes the exclusive rank and accepts an exhausted empty page', async () => {
    const fetch = sequence(profile, current, page(0), current, profile);
    expect(await readConfigurationIntakeLists(organizationId, actorId, boardId, new AbortController().signal, rank(50)))
      .toEqual({ board: { id: boardId, name: 'Owning Board' }, lists: [], nextAfterRank: null });
    expect(fetch.mock.calls[2][0]).toContain(`?afterRank=${rank(50)}`);
  });
  it.each(['', '1', '١'.repeat(30), '1'.repeat(31)])('rejects an invalid local boundary before reading %s', async cursor => {
    const fetch = sequence();
    expect(() => readConfigurationIntakeLists(organizationId, actorId, boardId, new AbortController().signal, cursor)).toThrow();
    expect(fetch).not.toHaveBeenCalled();
  });
  it.each([
    () => ({ ...page(), organizationId: actorId }),
    () => ({ ...page(), board: { ...page().board, id: actorId } }),
    () => ({ ...page(), cards: [{ title: 'Private Card' }] }),
    () => ({ ...page(), items: [row(1), row(1)] }),
    () => ({ ...page(), items: [row(2), row(1)] }),
    () => ({ ...page(), items: [{ ...row(1), version: 0 }] }),
    () => ({ ...page(), items: [{ ...row(1), rank: '١'.repeat(30) }] }),
    () => ({ ...page(), items: [{ ...row(1), boardId: actorId }] }),
    () => ({ ...page(50), nextAfterRank: rank(49) }),
    () => ({ ...page(), nextAfterRank: rank(1) }),
    () => page(51),
  ])('withholds malformed, foreign, unordered, oversized or private source shapes', async source => {
    sequence(profile, current, source());
    await expect(readConfigurationIntakeLists(organizationId, actorId, boardId, new AbortController().signal)).rejects.toMatchObject({ status: 503 });
  });
  it('rejects a nonexclusive returned row', async () => {
    sequence(profile, current, page());
    await expect(readConfigurationIntakeLists(organizationId, actorId, boardId, new AbortController().signal, rank(1))).rejects.toMatchObject({ status: 503 });
  });
  it('withholds a validated page after the active account is replaced', async () => {
    sequence(profile, current, page(), current, { ...profile, id: boardId });
    await expect(readConfigurationIntakeLists(organizationId, actorId, boardId, new AbortController().signal)).rejects.toMatchObject({ status: 401 });
  });
});
