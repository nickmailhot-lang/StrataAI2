import { validateOrganizationMetadataSync } from './organizationMetadataSync';

const org = '11111111-1111-4111-8111-111111111111', user = '22222222-2222-4222-8222-222222222222';
const actor = '33333333-3333-4333-8333-333333333333', id = '44444444-4444-4444-8444-444444444444';
const event = { eventId: id, eventType: 'ORGANIZATION_UPDATED', actorId: actor, organizationId: org,
  boardId: null, entityType: 'Organization', entityId: org, version: 2, createdAt: '2026-10-06T12:00:00Z', metadata: {} };
const frame = { organizationId: org, userId: user, page: { cursor: 'protected-metadata_A', events: [event],
  hasMore: false, pending: false, resetRequired: false } };

it('accepts a canonical event from another actor and deduplicates its stable source identity', () => {
  const parsed = validateOrganizationMetadataSync(frame, org, user, new Map())!;
  expect(parsed.eventIds).toHaveLength(1);
  expect(validateOrganizationMetadataSync(frame, org, user, new Map(parsed.eventIds))!.eventIds).toEqual([]);
});
it.each([
  { ...frame, userId: actor }, { ...frame, organizationId: user },
  { ...frame, page: { ...frame.page, cursor: '123' } },
  { ...frame, page: { ...frame.page, cursor: 'x'.repeat(4097) } },
  { ...frame, page: { ...frame.page, hasMore: true } },
  { ...frame, page: { ...frame.page, resetRequired: true } },
  { ...frame, page: { ...frame.page, events: [event, event] } },
  ...[
    { name: 'Private name' }, { actorId: 'invalid' }, { organizationId: user }, { entityId: user },
    { boardId: actor }, { metadata: { name: 'Private name' } }, { metadata: [] }, { version: 0 },
    { eventType: 'ORGANIZATION_CREATED', version: 2 }, { eventType: 'ORGANIZATION_UPDATED', version: 1 },
    { eventType: 'ORGANIZATION_DELETED' }, { createdAt: '2026-02-30T12:00:00Z' },
  ].map(change => ({ ...frame, page: { ...frame.page, events: [{ ...event, ...change }] } })),
])('rejects content leakage, wrong authority, corrupt envelopes and invalid replay semantics', input => {
  expect(validateOrganizationMetadataSync(input, org, user, new Map())).toBeNull();
});
it.each([{ actorId: user }, { version: 3 }, { createdAt: '2026-10-06T12:00:01Z' }])(
  'refuses a previously seen source whose canonical attribution, version or timestamp changes', change => {
    const seen = new Map(validateOrganizationMetadataSync(frame, org, user, new Map())!.eventIds);
    expect(validateOrganizationMetadataSync({ ...frame, page: { ...frame.page, events: [{ ...event, ...change }] } }, org, user, seen)).toBeNull();
  });
it('accepts reset without history and empty heartbeats without numeric cursor inference', () => {
  const page = { ...frame.page, events: [], resetRequired: true };
  expect(validateOrganizationMetadataSync({ ...frame, page }, org, user, new Map())!.resetRequired).toBe(true);
  expect(validateOrganizationMetadataSync({ ...frame, page: { ...page, resetRequired: false, cursor: 'protected-metadata_B' } }, org, user, new Map())!.eventIds).toEqual([]);
});
