import { emptyLot, forgetLotChanges, LotIntent, lotErrors, parseLot, readLots, readLotHistory, retainLotIntent, recoveryKey } from './strataLotClient';
const org = '11111111-1111-4111-8111-111111111111', actor = '22222222-2222-4222-8222-222222222222';
const lotId = '33333333-3333-4333-8333-333333333333';
const profile = { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'America/Vancouver' };
const data = { ...emptyLot, unitIdentifier: '101', lotIdentifier: 'SL1' };
const lot = { id: lotId, organizationId: org, version: 1, data, createdAt: '2026-10-28T00:00:00Z', updatedAt: '2026-10-28T00:00:00Z' };
const reply = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
function stub(delegate: (path: string, options: RequestInit) => Response | Promise<Response>, role = 0) {
  vi.stubGlobal('fetch', vi.fn((path: string, options: RequestInit = {}) => path === '/me' ? Promise.resolve(reply(profile))
    : path === `/organizations/${org}` ? Promise.resolve(reply({ organization: { id: org, status: 0 }, role })) : Promise.resolve(delegate(path, options))));
}
afterEach(() => { vi.unstubAllGlobals(); sessionStorage.clear(); });
it('PRD-28 LOT-FR-001/002: parses only canonical tenant-bound records', () => {
  expect(parseLot(lot, org, lotId)).toEqual(lot);
  expect(() => parseLot({ ...lot, organizationId: actor }, org)).toThrow();
  expect(() => parseLot({ ...lot, version: 0 }, org)).toThrow();
});
it.each(['unitIdentifier', 'lotIdentifier', 'civicAddress', 'building', 'floor', 'type'] as const)('refuses invalid %s without normalizing historical labels', field => {
  expect(lotErrors({ ...data, [field]: ' bad ' })[field]).toBeDefined();
  expect(lotErrors({ ...data, [field]: 'bad\u200b' })[field]).toBeDefined();
});
it('LOT-FR-005: does not create a fake common-property unit or owner field', () => {
  expect(Object.keys(lotErrors(data))).toEqual([]);
  expect(() => parseLot({ ...lot, data: { ...data, owner: actor } }, org)).toThrow();
});
it('PRD-28-TC-01: exposes the exact next-page cursor after 50 records', async () => {
  const items = Array.from({ length: 50 }, (_, index) => ({ ...lot, id: `33333333-3333-4333-8333-${String(index + 1).padStart(12, '0')}` }));
  stub(() => reply({ organizationId: org, items, nextCursor: items[49].id }));
  const result = await readLots(org, new AbortController().signal);
  expect(result.items).toHaveLength(50); expect(result.nextCursor).toBe(items[49].id);
});
it.each(['duplicate', 'backwards', 'foreign', 'wrong cursor'])('rejects %s directory responses', async variant => {
  let items = [lot]; let nextCursor: string | null = null;
  if (variant === 'duplicate') items = [lot, lot];
  if (variant === 'backwards') items = [lot, { ...lot, id: actor }];
  if (variant === 'foreign') items = [{ ...lot, organizationId: actor }];
  if (variant === 'wrong cursor') nextCursor = actor;
  stub(() => reply({ organizationId: org, items, nextCursor }));
  await expect(readLots(org, new AbortController().signal)).rejects.toThrow();
});
it.each([2, 3])('PRD-28-TC-04/05: role %i cannot fetch the administrative directory', async role => {
  const request = vi.fn(() => reply({ organizationId: org, items: [lot], nextCursor: null })); stub(request, role);
  await expect(readLots(org, new AbortController().signal)).rejects.toMatchObject({ status: 404 }); expect(request).not.toHaveBeenCalled();
});
it('LOT-FR-006: validates descending history boundaries and stable identity', async () => {
  const revision = (version: number) => ({ lot: { ...lot, version }, actorId: actor, eventId: actor, correlationId: 'safe' });
  stub(() => reply({ organizationId: org, strataLotId: lotId, items: [revision(2), revision(1)], nextBeforeVersion: null }));
  expect((await readLotHistory(org, actor, lotId, new AbortController().signal, 3)).items.map(r => r.lot.version)).toEqual([2, 1]);
  await expect(readLotHistory(org, actor, lotId, new AbortController().signal, 2)).rejects.toThrow();
});
it('PRD-28-TC-07/15: preserves exact original bytes/key across a lost response and reload', async () => {
  const writes: RequestInit[] = [];
  stub((_path, options) => { writes.push(options); if (writes.length === 1) return Promise.reject(new TypeError('lost response')); return reply(lot); });
  const intent = new LotIntent(org, actor, lotId, 0, data); retainLotIntent(intent);
  await expect(intent.submit(new AbortController().signal)).rejects.toThrow();
  const restored = LotIntent.restore(sessionStorage.getItem(recoveryKey(org, actor))!);
  expect(await restored.submit(new AbortController().signal)).toEqual(lot);
  expect(writes[1].body).toBe(writes[0].body);
  expect(new Headers(writes[1].headers).get('Idempotency-Key')).toBe(new Headers(writes[0].headers).get('Idempotency-Key'));
  expect(() => retainLotIntent(new LotIntent(org, actor, lotId, 0, { ...data, unitIdentifier: '102' }))).toThrow();
});
it('PRD-28-TC-06: final role withdrawal suppresses a response that was already read', async () => {
  let admissions = 0;
  vi.stubGlobal('fetch', vi.fn(async (path: string) => path === '/me' ? reply(profile) : path === `/organizations/${org}`
    ? reply({ organization: { id: org, status: 0 }, role: ++admissions === 1 ? 0 : 2 })
    : reply({ organizationId: org, items: [lot], nextCursor: null })));
  await expect(readLots(org, new AbortController().signal)).rejects.toMatchObject({ status: 404 });
});
it('PRD-28-TC-06: changed account cannot replay an original', async () => {
  const writes = vi.fn(); vi.stubGlobal('fetch', async () => { writes(); return reply({ ...profile, id: lotId }); });
  await expect(new LotIntent(org, actor, lotId, 0, data).submit(new AbortController().signal)).rejects.toMatchObject({ status: 401 });
  expect(writes).toHaveBeenCalledTimes(1);
});
it('UXA-03: withdrawal removes only the denied Organization and account switch removes foreign originals', () => {
  retainLotIntent(new LotIntent(org, actor, lotId, 0, data));
  retainLotIntent(new LotIntent(lotId, actor, org, 0, data));
  forgetLotChanges(org); expect(sessionStorage.getItem(recoveryKey(org, actor))).toBeNull();
  expect(sessionStorage.getItem(recoveryKey(lotId, actor))).not.toBeNull();
  forgetLotChanges(undefined, lotId); expect(sessionStorage.length).toBe(0);
});
