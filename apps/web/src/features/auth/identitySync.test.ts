import { validateIdentitySync } from './identitySync';

const profile = { id: 'subject', version: 2 };
const isProfile = (value: unknown): value is typeof profile => !!value && typeof value === 'object'
  && (value as typeof profile).id === 'subject' && (value as typeof profile).version === 2;
const event = { eventId: '11111111-1111-1111-1111-111111111111', sequence: 2,
  eventType: 'USER_PROFILE_UPDATED', actorId: 'subject', entityType: 'User', entityId: 'subject',
  version: 2, organizationId: null, boardId: null, metadata: {}, createdAt: '2026-03-08T10:30:00Z' };
const snapshot = { profile, cursor: 2, latestSequence: 2, hasMore: false, events: [event] };

describe('PRD-02/60 identity replay validation', () => {
  it('accepts a snapshot handoff and contiguous replay', () => {
    expect(validateIdentitySync({ ...snapshot, events: [] }, undefined, isProfile, new Set())?.cursor).toBe(2);
    expect(validateIdentitySync(snapshot, 1, isProfile, new Set())?.eventIds).toEqual([event.eventId]);
  });
  it.each([
    { ...snapshot, cursor: 3 }, { ...snapshot, hasMore: true },
    { ...snapshot, events: [{ ...event, sequence: 3 }] },
    { ...snapshot, events: [{ ...event, entityId: 'other' }] },
    { ...snapshot, events: [{ ...event, metadata: { email: 'private@example.test' } }] },
    { ...snapshot, events: [{ ...event, boardId: 'invented-board' }] },
    { ...snapshot, events: [{ ...event, version: 3 }] },
  ])('rejects malformed replay %# without acknowledging its cursor', invalid => {
    expect(validateIdentitySync(invalid, 1, isProfile, new Set())).toBeUndefined();
  });
  it('rejects duplicate event IDs and historical events during initial handoff', () => {
    expect(validateIdentitySync(snapshot, 1, isProfile, new Set([event.eventId]))).toBeUndefined();
    expect(validateIdentitySync(snapshot, undefined, isProfile, new Set())).toBeUndefined();
  });
});
