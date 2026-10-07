import { validateInvitationRecipientSync } from './invitationRecipientSync';
const event = { eventId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', eventType: 'INVITATION_CREATED',
  sequence: '42', createdAt: '2026-10-07T00:00:00.1234567Z' };
const reset = { cursor: 'opaque_head', events: [], hasMore: false, resetRequired: true };
const page = { ...reset, cursor: 'opaque_next', events: [event], resetRequired: false };
const read = (value: unknown, cursor: string | undefined = 'opaque_before', sequence?: bigint, seen = new Set<string>()) =>
  validateInvitationRecipientSync(value, cursor, sequence, seen);

it('accepts empty bootstrap/reset without decoding the opaque captured head', () => {
  expect(validateInvitationRecipientSync(reset, undefined, undefined, new Set())).toEqual({ cursor: reset.cursor,
    resetRequired: true, lastSequence: undefined, eventIds: [] });
  expect(read(reset, 'old_expired', 100n, new Set([event.eventId]))?.lastSequence).toBeUndefined();
  expect(validateInvitationRecipientSync(page, undefined, undefined, new Set())).toBeNull();
});
it('preserves original event IDs and checks continuity after the first actual event', () => {
  expect(read(page)?.eventIds).toEqual([event.eventId]);
  expect(read(page, 'opaque_before', 41n)?.lastSequence).toBe(42n);
  expect(read(page, 'opaque_before', 40n)).toBeNull();
  expect(read({ ...reset, cursor: 'rotated_heartbeat', resetRequired: false }, 'opaque_before', 42n)?.lastSequence).toBe(42n);
  expect(read(page, 'opaque_before', 41n, new Set([event.eventId]))).toBeNull();
});
it.each([
  { ...page, organizationId: event.eventId }, { ...page, actorId: event.eventId },
  { ...page, cursor: '' }, { ...page, cursor: ' ' }, { ...page, cursor: 'x'.repeat(4097) },
  { ...page, cursor: 1 }, { ...page, hasMore: true }, { ...page, resetRequired: true },
  { ...reset, hasMore: true }, { ...page, events: [event, event] },
  { ...page, events: [event, { ...event, eventId: event.eventId.toUpperCase(), sequence: '43' }] },
  { ...page, events: [{ ...event, sequence: 0 }] }, { ...page, events: [{ ...event, sequence: 1.5 }] },
  { ...page, events: [{ ...event, sequence: Number.MAX_SAFE_INTEGER + 1 }] },
  { ...page, events: [{ ...event, sequence: '9223372036854775808' }] },
  { ...page, events: [{ ...event, sequence: '01' }] },
  { ...page, events: [{ ...event, sequence: '0' }] },
  { ...page, events: [{ ...event, sequence: '-1' }] },
  { ...page, events: [{ ...event, sequence: '1.5' }] },
  { ...page, events: [{ ...event, eventId: '00000000-0000-0000-0000-000000000000' }] },
  { ...page, events: [{ ...event, createdAt: '2026-02-30T00:00:00Z' }] },
  { ...page, events: [{ ...event, createdAt: '2026-10-07T00:00:00.12345678Z' }] },
  { ...page, events: [{ ...event, eventType: 'BOARD_CREATED' }] },
  { ...page, events: [{ ...event, email: 'private@example.test' }] },
  { ...page, events: [{ ...event, invitationId: event.eventId }] },
  { ...page, events: [{ ...event, metadata: {} }] },
])('refuses malformed/private delivery before admitting any source: %j', value => {
  expect(read(value)).toBeNull();
});
it('admits a bounded contiguous full page and refuses incomplete hasMore or a gap', () => {
  const events = Array.from({ length: 50 }, (_, i) => ({ ...event, sequence: String(i + 1),
    eventId: `${String(i + 1).padStart(8, '0')}-aaaa-4aaa-8aaa-aaaaaaaaaaaa` }));
  expect(read({ ...page, events, hasMore: true }, 'start', 0n)?.eventIds).toHaveLength(50);
  expect(read({ ...page, events: [...events, { ...event, sequence: '51' }] })).toBeNull();
  expect(read({ ...page, events: [events[0], events[2]] })).toBeNull();
});
it('keeps adjacent sequences exact beyond JavaScript safe integers through the signed 64-bit maximum', () => {
  expect(read({ ...page, events: [{ ...event, sequence: '9007199254740993' }] }, 'before', 9007199254740992n)
    ?.lastSequence).toBe(9007199254740993n);
  expect(read({ ...page, events: [{ ...event, sequence: '9223372036854775807' }] }, 'before', 9223372036854775806n)
    ?.lastSequence).toBe(9223372036854775807n);
});
