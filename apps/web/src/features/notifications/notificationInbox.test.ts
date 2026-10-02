import { isNotificationProfile, notificationLabels, parseInbox, validateReadAcknowledgment } from './notificationInbox';
const org = '11111111-1111-1111-1111-111111111111', recipient = '22222222-2222-2222-2222-222222222222';
const actor = '33333333-3333-3333-3333-333333333333', board = '44444444-4444-4444-4444-444444444444', card = '55555555-5555-5555-5555-555555555555';
const id = (n: number) => `66666666-6666-6666-6666-${String(n).padStart(12, '0')}`;
const item = (n = 1, createdAt = '2026-10-02T10:00:00.000001Z') => ({ id: id(n), actorId: actor, recipientId: recipient,
  boardId: board, entityId: card, type: 'CARD_ASSIGNED', entityType: 'Card', createdAt, readAt: null as string | null,
  entityLink: `/app/${org}/boards/${board}/cards/${card}` });
const page = (items = [item()], nextCursor: string | null = null) => ({ organizationId: org, items, nextCursor });

it.each(Object.keys(notificationLabels))('accepts configured notification %s with its canonical Card link', type => {
  expect(parseInbox(page([{ ...item(), type }]), org, recipient).items[0].type).toBe(type);
});
it.each(['WATCH_CREATED', 'UNKNOWN', 'constructor', '__proto__'])('rejects unrelated or inherited notification type %s', type => {
  expect(() => parseInbox(page([{ ...item(), type }]), org, recipient)).toThrow();
});
it('accepts personal due reminders while retaining self-action suppression for every activity type', () => {
  expect(parseInbox(page([{ ...item(), type: 'REMINDER_FIRED', actorId: recipient }]), org, recipient).items[0].type).toBe('REMINDER_FIRED');
  for (const type of Object.keys(notificationLabels).filter(type => type !== 'REMINDER_FIRED')) {
    expect(() => parseInbox(page([{ ...item(), type, actorId: recipient }]), org, recipient)).toThrow();
  }
  expect(() => parseInbox(page([{ ...item(), type: 'REMINDER_FIRED', recipientId: actor }]), org, recipient)).toThrow();
});

it('preserves sub-millisecond ordering and timestamp/UUID seek boundaries', () => {
  const data = page([item(1, '2026-10-02T10:00:00.000002Z'), item(2)]);
  expect(parseInbox(data, org, recipient).items[0].createdTicks - parseInbox(data, org, recipient).items[1].createdTicks).toBe(10n);
  expect(() => parseInbox(data, org, recipient, '2026-10-02T10:00:00.0000020+00:00/' + id(1))).toThrow();
  expect(parseInbox(page([item(2)]), org, recipient, '2026-10-02T10:00:00.0000020+00:00/' + id(1)).items).toHaveLength(1);
  expect(() => parseInbox(page([item(1), item(2)]), org, recipient)).toThrow();
});
it('accepts only bounded pages with a cursor matching the final record', () => {
  const items = Array.from({ length: 50 }, (_, n) => item(50 - n));
  const next = `${items[49].createdAt}/${items[49].id}`;
  expect(parseInbox(page(items, next), org, recipient).nextCursor).toBe(next);
  expect(() => parseInbox(page(items, `${items[0].createdAt}/${items[0].id}`), org, recipient)).toThrow();
  expect(() => parseInbox(page([item()], next), org, recipient)).toThrow();
  expect(() => parseInbox(page([item(), ...items]), org, recipient)).toThrow();
});
it.each([
  { recipientId: actor }, { actorId: recipient }, { entityLink: 'https://example.test/private' },
  { entityType: 'Board' }, { type: 'UNKNOWN' }, { readAt: '2026-10-02T09:59:59Z' },
  { createdAt: '2026-02-30T10:00:00Z' }, { createdAt: '2026-10-02T10:00:00+01:00' },
])('rejects cross-recipient, unsafe links and malformed lifecycle data (%j)', change => {
  expect(() => parseInbox(page([{ ...item(), ...change }]), org, recipient)).toThrow();
});
it('requires an exact ordered acknowledgment of the original selection', () => {
  const targets = parseInbox(page([item(2), item(1)]), org, recipient).items.map(n => ({ id: n.id, createdTicks: n.createdTicks }));
  const items = [1, 2].map(n => ({ id: id(n), readAt: '2026-10-02T10:00:01Z' }));
  expect(() => validateReadAcknowledgment({ organizationId: org, items }, org, targets)).not.toThrow();
  for (const invalid of [items.slice(0, 1), [...items].reverse(), [items[0], items[0]], [...items, { ...items[0], id: id(3) }], [{ ...items[0], readAt: '2026-10-02T09:00:00Z' }, items[1]]]) {
    expect(() => validateReadAcknowledgment({ organizationId: org, items: invalid }, org, targets)).toThrow();
  }
});
it('validates active profile formatting settings before displaying private dates', () => {
  const profile = { id: recipient, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'America/Vancouver' };
  expect(isNotificationProfile(profile)).toBe(true);
  for (const change of [{ timezone: 'unknown' }, { status: 'DEACTIVATED' }, { version: 0 }, { locale: '?' }]) expect(isNotificationProfile({ ...profile, ...change })).toBe(false);
});
