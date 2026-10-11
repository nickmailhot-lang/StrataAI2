import { parseOperationalLifecycleFrame, parseOperationalLifecycleReplay } from './operationalLifecycleReplay';
const organization = '55555555-5555-4555-8555-555555555555';
const actor = '22222222-2222-4222-8222-222222222222';
const sourceActor = '33333333-3333-4333-8333-333333333333';
const fact = { eventId: '44444444-4444-4444-8444-444444444444', organizationId: organization, actorId: sourceActor,
  entityId: organization, entityType: 'Organization', version: 2, previousStatus: 'ACTIVE', status: 'SUSPENDED',
  eventType: 'ORGANIZATION_SUSPENDED', actingCapacity: 'ORGANIZATION_OWNER', createdAt: '2026-10-10T12:00:00.123456Z' };
const page = { organizationId: organization, actorId: actor, status: 'SUSPENDED', version: 2, afterVersion: 0,
  nextVersion: null, pending: false, events: [fact] };
const parse = (input: unknown, after = 0) => parseOperationalLifecycleReplay(input, organization, actor, after);
it('keeps original source actor distinct from authenticated recipient and copies validated facts', () => {
  const parsed = parse(page); expect(parsed).toEqual(page); expect(parsed?.events[0]).not.toBe(fact);
});
it('binds exact frame, tenant, recipient and requested replay position', () => {
  const frame = { organizationId: organization, userId: actor, page };
  expect(parseOperationalLifecycleFrame(frame, organization, actor, 0)).toEqual(page);
  for (const change of [{ organizationId: sourceActor }, { userId: sourceActor }, { privateName: 'private' }])
    expect(parseOperationalLifecycleFrame({ ...frame, ...change }, organization, actor, 0)).toBeUndefined();
  for (const change of [{ organizationId: sourceActor }, { actorId: sourceActor }, { afterVersion: 1 }, { privateName: 'private' }])
    expect(parse({ ...page, ...change })).toBeUndefined();
});
it('accepts pending initial publication without advancing the cursor or mistaking source for current state', () => {
  expect(parse({ ...page, events: [], pending: true })).toBeDefined();
  const later = { ...page, status: 'ACTIVE', version: 4, nextVersion: 2, pending: true };
  expect(parse(later)?.status).toBe('ACTIVE'); expect(parse(later)?.events[0].status).toBe('SUSPENDED');
  expect(parse({ ...later, nextVersion: null })).toBeUndefined();
});
it('rejects cursor skips, unsafe numeric positions and continuations without a delivered source', () => {
  for (const change of [{ version: Number.MAX_SAFE_INTEGER }, { version: 1.1 }, { nextVersion: 1 }, { nextVersion: 3 },
    { nextVersion: 2 }, { afterVersion: -1 }, { afterVersion: Number.NaN }]) expect(parse({ ...page, ...change })).toBeUndefined();
  expect(parse({ ...page, events: [], nextVersion: 1 })).toBeUndefined();
  expect(parse(page, 1)).toBeUndefined();
});
it('validates exact historical source shape, transition kind, capacity, UTC microsecond clock and tenant', () => {
  for (const change of [{ entityId: actor }, { organizationId: actor }, { entityType: 'Board' }, { actorId: 'invalid' },
    { eventId: '00000000-0000-0000-0000-000000000000' }, { eventType: 'ORGANIZATION_DEACTIVATED' },
    { actingCapacity: 'MEMBER' }, { previousStatus: 'SUSPENDED' }, { status: 'DELETED' }, { version: 0 },
    { version: 3 }, { createdAt: '2026-02-31T12:00:00Z' }, { createdAt: '2026-10-10T12:00:00.1234567Z' },
    { createdAt: '2026-10-10T12:00:00-07:00' }, { name: 'private' }])
    expect(parse({ ...page, events: [{ ...fact, ...change }] })).toBeUndefined();
});
it('rejects duplicate event identities, reversed versions and broken transition chains', () => {
  const second = { ...fact, eventId: '66666666-6666-4666-8666-666666666666', version: 3,
    previousStatus: 'SUSPENDED', status: 'ACTIVE', eventType: 'ORGANIZATION_REACTIVATED' };
  const two = { ...page, status: 'ACTIVE', version: 3, events: [fact, second] };
  expect(parse(two)).toBeDefined();
  for (const change of [{ eventId: fact.eventId }, { version: 2 }, { previousStatus: 'DEACTIVATED' }])
    expect(parse({ ...two, events: [fact, { ...second, ...change }] })).toBeUndefined();
  expect(parse({ ...two, events: [second, fact] })).toBeUndefined();
});
it('requires the final source at the parent revision to agree with current lifecycle', () => {
  expect(parse({ ...page, status: 'ACTIVE' })).toBeUndefined();
  expect(parse({ ...page, version: 3, status: 'ACTIVE' })).toBeDefined();
});
it('accepts bounded 50/1 recovery with intervening non-lifecycle revisions and enforces the page bound', () => {
  const events = Array.from({ length: 51 }, (_, index) => ({ ...fact,
    eventId: `${(index + 10).toString(16).padStart(8, '0')}-4444-4444-8444-444444444444`, version: 2 + index * 2,
    previousStatus: index % 2 ? 'SUSPENDED' : 'ACTIVE', status: index % 2 ? 'ACTIVE' : 'SUSPENDED',
    eventType: index % 2 ? 'ORGANIZATION_REACTIVATED' : 'ORGANIZATION_SUSPENDED' }));
  const first = { ...page, version: 102, nextVersion: 100, events: events.slice(0, 50) };
  expect(parse(first)?.nextVersion).toBe(100);
  expect(parse({ ...page, version: 102, afterVersion: 100, events: events.slice(50) }, 100)?.events).toHaveLength(1);
  expect(parse({ ...page, version: 102, events })).toBeUndefined();
});
it('accepts empty heartbeat at the actual cursor and rejects malformed collections', () => {
  expect(parse({ ...page, afterVersion: 2, events: [] }, 2)).toBeDefined();
  for (const events of [null, {}, [null], [[]]]) expect(parse({ ...page, events })).toBeUndefined();
  expect(parse(null)).toBeUndefined(); expect(parse([])).toBeUndefined();
});
