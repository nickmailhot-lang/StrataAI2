import { validateOrganizationConfigurationSync as validate } from './organizationConfigurationSync';

const organizationId = '11111111-1111-4111-8111-111111111111', userId = '22222222-2222-4222-8222-222222222222';
const actorId = '33333333-3333-4333-8333-333333333333';
function source(version = 1) {
  return { eventId: `44444444-4444-4444-8444-${String(version).padStart(12, '0')}`, organizationId, actorId, version,
    correlationId: 'configuration-source-reference', createdAt: '2026-10-10T12:00:00.0000001Z',
    entityType: 'OrganizationConfiguration', entityId: organizationId, eventType: 'ORGANIZATION_CONFIGURATION_CHANGED' };
}
const page = { cursor: 'protected-configuration_A', events: [source()], hasMore: false, pending: false, resetRequired: false };
const frame = { organizationId, userId, page };
it('accepts the original first configuration source and preserves sub-microsecond identity for deduplication', () => {
  const parsed = validate(frame, organizationId, userId, new Map())!;
  expect(parsed.eventIds).toHaveLength(1);
  expect(validate(frame, organizationId, userId, new Map(parsed.eventIds))!.eventIds).toEqual([]);
});
it.each([
  { actorId: userId }, { version: 2 }, { correlationId: 'changed-reference' }, { createdAt: '2026-10-10T12:00:00.0000002Z' },
])('rejects a previously seen identity whose original source attribution changes', change => {
  const seen = new Map(validate(frame, organizationId, userId, new Map())!.eventIds);
  expect(validate({ ...frame, page: { ...page, events: [{ ...source(), ...change }] } }, organizationId, userId, seen)).toBeNull();
});
it.each([
  { legalName: 'Private legal name' }, { civicAddress: 'Private address' }, { configuration: {} }, { metadata: {} }, { boardId: null },
  { actorId: 'invalid' }, { eventId: 'invalid' }, { organizationId: userId }, { entityId: userId },
  { entityType: 'Organization' }, { eventType: 'ORGANIZATION_UPDATED' }, { version: 0 }, { version: 1.5 },
  { version: Number.MAX_SAFE_INTEGER + 1 }, { createdAt: '2026-02-30T12:00:00Z' }, { createdAt: 'invalid' },
  { correlationId: '' }, { correlationId: ' '.repeat(2) }, { correlationId: 'x'.repeat(257) }, { correlationId: 'line\nbreak' },
])('rejects private fields, general metadata, guessed context and invalid source values', change => {
  expect(validate({ ...frame, page: { ...page, events: [{ ...source(), ...change }] } }, organizationId, userId, new Map())).toBeNull();
});
it.each([
  { ...frame, organizationId: userId }, { ...frame, userId: actorId }, { ...frame, unexpected: true },
  ...[{ cursor: '123' }, { cursor: 'x'.repeat(4097) }, { cursor: 'bad cursor' }, { hasMore: true },
    { resetRequired: true }, { pending: 'true' }, { events: [source(), source()] },
    { events: [source(1), source(3)] }, { events: [source(2), source(1)] }, { unexpected: true }]
    .map(change => ({ ...frame, page: { ...page, ...change } })),
])('rejects invalid recipient envelopes, cursor bounds and discontinuous source order', input => {
  expect(validate(input, organizationId, userId, new Map())).toBeNull();
});
it('accepts reset, pending publication and exactly bounded contiguous continuation without inferring ciphertext position', () => {
  const empty = { ...page, events: [], resetRequired: true };
  expect(validate({ ...frame, page: empty }, organizationId, userId, new Map())!.resetRequired).toBe(true);
  expect(validate({ ...frame, page: { ...empty, resetRequired: false, pending: true } }, organizationId, userId, new Map())!.eventIds).toEqual([]);
  const events = Array.from({ length: 50 }, (_, index) => source(index + 7));
  expect(validate({ ...frame, page: { ...page, events, hasMore: true } }, organizationId, userId, new Map())!.eventIds).toHaveLength(50);
  expect(validate({ ...frame, page: { ...page, events, pending: true } }, organizationId, userId, new Map())!.eventIds).toHaveLength(50);
  expect(validate({ ...frame, page: { ...page, events, pending: true, hasMore: true } }, organizationId, userId, new Map())).toBeNull();
  expect(validate({ ...frame, page: { ...page, events: [...events, source(57)] } }, organizationId, userId, new Map())).toBeNull();
});
