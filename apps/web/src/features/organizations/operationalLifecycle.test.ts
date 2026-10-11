import { parseLifecycleAcknowledgment, parseLifecycleReview, readLifecycleIntent, saveLifecycleIntent } from './operationalLifecycle';
const org = '55555555-5555-4555-8555-555555555555'; const actor = '22222222-2222-4222-8222-222222222222';
const intent = { organizationId: org, actorId: actor, key: '33333333-3333-4333-8333-333333333333', status: 'SUSPENDED' as const,
  previousStatus: 'ACTIVE' as const, version: 3, actingCapacity: 'ORGANIZATION_OWNER' as const };
const review = { organizationId: org, actorId: actor, status: 'ACTIVE', version: 3, updatedAt: '2026-10-10T12:00:00Z', actingCapacity: 'ORGANIZATION_OWNER', name: 'Reviewed Organization' };
const acknowledgment = { organizationId: org, actorId: actor, eventId: intent.key, actorLabel: 'Original actor', actingCapacity: intent.actingCapacity,
  previousStatus: 'ACTIVE', status: 'SUSPENDED', version: 4, correlationId: 'original-source', createdAt: '2026-10-10T12:00:01Z' };
afterEach(() => sessionStorage.clear());
it('validates exact current review scope and rejects withdrawn/malformed private sources', () => {
  expect(parseLifecycleReview(review, org, actor)).toEqual(review);
  expect(parseLifecycleReview({ ...review, updatedAt: '2026-10-10T12:00:00.1234567+00:00' }, org, actor)).toBeDefined();
  expect(parseLifecycleReview({ ...review, updatedAt: '2026-02-31T12:00:00Z' }, org, actor)).toBeUndefined();
  for (const change of [{ actorId: org }, { organizationId: actor }, { status: 'DELETED' }, { version: 0 }, { version: 1.5 },
    { updatedAt: 'invalid' }, { actingCapacity: 'MEMBER' }, { extra: 'Private content' }]) expect(parseLifecycleReview({ ...review, ...change }, org, actor)).toBeUndefined();
});
it('requires original actor/state/revision/capacity/time and bounded caption in acknowledgment', () => {
  expect(parseLifecycleAcknowledgment(acknowledgment, intent)).toEqual(acknowledgment);
  for (const change of [{ actorId: org }, { organizationId: actor }, { status: 'ACTIVE' }, { previousStatus: 'DEACTIVATED' },
    { version: 5 }, { actorLabel: '\0private' }, { actorLabel: '' }, { actingCapacity: 'MEMBER' }, { createdAt: 'invalid' },
    { createdAt: '2026-10-10T12:00:01.1234567Z' }, { correlationId: '' }, { extra: 'Private content' }]) expect(parseLifecycleAcknowledgment({ ...acknowledgment, ...change }, intent)).toBeUndefined();
});
it('stores only bounded original retry metadata and refuses corrupt or foreign continuations', () => {
  expect(saveLifecycleIntent(intent)).toBe(true); expect(readLifecycleIntent(org)).toEqual(intent); expect(readLifecycleIntent(actor)).toBeUndefined();
  for (const change of [{ actorId: 'bad' }, { version: Number.MAX_SAFE_INTEGER }, { status: 'ACTIVE' }, { extra: 'private' }]) {
    sessionStorage.setItem(`strataai.operational-lifecycle.v1:${org}`, JSON.stringify({ ...intent, ...change })); expect(readLifecycleIntent(org)).toBeUndefined();
  }
});
