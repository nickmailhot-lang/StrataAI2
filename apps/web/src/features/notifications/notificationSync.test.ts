import { validateNotificationSync } from './notificationSync';

const org = '11111111-1111-1111-1111-111111111111';
const recipient = '22222222-2222-2222-2222-222222222222';
const event = { eventId: '33333333-3333-3333-3333-333333333333', actorId: org, recipientId: recipient,
  organizationId: org, boardId: '44444444-4444-4444-4444-444444444444', entityType: 'Notification',
  entityId: '55555555-5555-5555-5555-555555555555', eventType: 'NOTIFICATION_CREATED', version: 1,
  sequence: '9007199254740993', createdAt: '2026-10-04T12:00:00.0000001Z', metadata: {} };
const page = { organizationId: org, recipientId: recipient, cursor: event.sequence, hasMore: false,
  resetRequired: false, events: [event] };
const parse = (value: unknown, after: string | undefined = '9007199254740992', seen = new Set<string>()) =>
  validateNotificationSync(value, org, recipient, after, seen);

describe('private notification replay validation', () => {
  it('preserves decimal sequences above JavaScript integer precision', () => {
    expect(parse(page)).toEqual({ cursor: event.sequence, eventIds: [event.eventId], resetRequired: false });
  });
  it('accepts bounded hidden windows and empty cursor advancement', () => {
    expect(parse({ ...page, cursor: '50', events: [], hasMore: true }, '0')?.cursor).toBe('50');
    expect(parse({ ...page, cursor: '51', events: [] }, '50')?.cursor).toBe('51');
  });
  it('accepts a head snapshot and only a backwards empty reset', () => {
    expect(validateNotificationSync({ ...page, events: [] }, org, recipient, undefined, new Set())?.cursor).toBe(event.sequence);
    expect(parse({ ...page, cursor: '1', events: [], resetRequired: true }, '2')?.resetRequired).toBe(true);
    expect(parse({ ...page, cursor: '2', events: [], resetRequired: true }, '2')).toBeUndefined();
    expect(validateNotificationSync({ ...page, events: [], resetRequired: true }, org, recipient, undefined, new Set())).toBeUndefined();
    expect(validateNotificationSync(page, org, recipient, undefined, new Set())).toBeUndefined();
  });
  it.each([
    { ...page, recipientId: org }, { ...page, organizationId: recipient }, { ...page, cursor: Number(event.sequence) },
    { ...page, cursor: '9223372036854775808' }, { ...page, cursor: '09007199254740993' },
    { ...page, hasMore: true }, { ...page, resetRequired: true },
    ...[
      { recipientId: org }, { organizationId: recipient }, { entityType: 'Card' }, { sequence: '9007199254740992' },
      { sequence: '9007199254740994' }, { metadata: { title: 'Private title' } }, { metadata: [] },
      { eventType: 'NOTIFICATION_READ', version: 2 }, { version: 2 }, { eventId: 'invalid' },
      { createdAt: '2026-02-30T12:00:00Z' }, { createdAt: '2026-10-04T12:00:00' },
    ].map(change => ({ ...page, events: [{ ...event, ...change }] })),
  ])('rejects malformed or foreign replay %# before acknowledging it', value => {
    expect(parse(value)).toBeUndefined();
  });
  it('rejects duplicate identities and accepts recipient read transitions', () => {
    expect(parse(page, '9007199254740992', new Set([event.eventId]))).toBeUndefined();
    expect(parse({ ...page, events: [event, event] })).toBeUndefined();
    expect(parse({ ...page, events: [{ ...event, eventType: 'NOTIFICATION_READ', version: 2, actorId: recipient }] })).toBeDefined();
  });
  it.each(['title', 'body', 'entityLink', 'cardId', 'email', 'diagnostic', 'readAt'])('rejects unexpected private fields before accepting the cursor (%s)', field => {
    expect(parse({ ...page, [field]: 'private material' })).toBeUndefined();
    expect(parse({ ...page, events: [{ ...event, [field]: 'private material' }] })).toBeUndefined();
  });
});
