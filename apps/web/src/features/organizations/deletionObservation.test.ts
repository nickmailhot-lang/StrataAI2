import { clearDeletionRecovery, parseDeletionObservation, readDeletionRecovery, saveDeletionRecovery } from './deletionObservation';
const intent = { key: '55555555-5555-4555-8555-555555555555', actor: '22222222-2222-4222-8222-222222222222', version: 3 };
const complete = { requestId: intent.key, state: 'COMPLETED', version: 5, eventId: '77777777-7777-4777-8777-777777777777', completedAt: '2026-10-06T18:00:00.1234567+00:00' };
afterEach(() => sessionStorage.clear());
it.each([{ ...complete, requestId: intent.actor }, { ...complete, version: 4 }, { ...complete, eventId: null },
  { ...complete, eventId: '00000000-0000-0000-0000-000000000000' }, { ...complete, completedAt: 'yesterday' },
  { ...complete, state: 'UNKNOWN' }, { ...complete, privateName: 'never display' }])('rejects mismatched or malformed completion evidence %#', value => {
  expect(parseDeletionObservation(value, intent)).toBeUndefined();
});
it('accepts only correctly bound pending and terminal observations', () => {
  expect(parseDeletionObservation(complete, intent)).toEqual(complete);
  const pending = { requestId: intent.key, state: 'PENDING', version: 4, eventId: null, completedAt: null };
  expect(parseDeletionObservation(pending, intent)).toEqual(pending);
  expect(parseDeletionObservation({ ...pending, eventId: complete.eventId }, intent)).toBeUndefined();
});
it('bounds tab-local recovery and isolates references by Organization', () => {
  saveDeletionRecovery(intent.key, intent, true); expect(readDeletionRecovery(intent.key)).toEqual({ ...intent, acknowledged: true });
  expect(readDeletionRecovery(intent.actor)).toBeUndefined(); clearDeletionRecovery(intent.key); expect(readDeletionRecovery(intent.key)).toBeUndefined();
  sessionStorage.setItem(`strataai.organization-deletion.v1:${intent.key}`, 'x'.repeat(257)); expect(readDeletionRecovery(intent.key)).toBeUndefined();
  sessionStorage.setItem(`strataai.organization-deletion.v1:${intent.key}`, JSON.stringify({ ...intent, acknowledged: true, name: 'private' }));
  expect(readDeletionRecovery(intent.key)).toBeUndefined();
});
