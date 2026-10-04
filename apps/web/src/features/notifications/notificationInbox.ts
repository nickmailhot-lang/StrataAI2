export type NotificationProfile = { id: string; version: number; locale: string; timezone: string; status: 'ACTIVE'; emailVerified: boolean };
export const notificationLabels = {
  CARD_ASSIGNED: 'Assigned to you', CARD_CREATED: 'Card created', CARD_COPIED: 'Card copied', CARD_UPDATED: 'Card updated', CARD_MOVED: 'Card moved',
  CARD_ARCHIVED: 'Card archived', CARD_RESTORED: 'Card restored', CARD_MEMBER_ADDED: 'Card member added',
  CARD_MEMBER_REMOVED: 'Card member removed', LABEL_ADDED: 'Label added', LABEL_REMOVED: 'Label removed',
  CARD_DATE_CHANGED: 'Card dates changed', CARD_DUE_COMPLETED: 'Due date completed', CARD_DUE_REOPENED: 'Due date reopened',
  REMINDER_FIRED: 'Due date reminder',
  MENTION_CREATED: 'Mentioned you in a comment',
} as const;
export type NotificationType = keyof typeof notificationLabels;
function notificationType(value: unknown): value is NotificationType {
  return typeof value === 'string' && Object.hasOwn(notificationLabels, value);
}
export type InboxItem = { id: string; type: NotificationType; actorId: string; recipientId: string; boardId: string; currentBoardId?: string; entityId: string; entityLink: string;
  createdAt: string; createdTicks: bigint; readAt: string | null };
export type InboxPage = { items: InboxItem[]; nextCursor: string | null };
const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
export const notificationUuid = (value: unknown): value is string => typeof value === 'string' && uuidPattern.test(value) && value !== '00000000-0000-0000-0000-000000000000';

// Preserve PostgreSQL microseconds/.NET ticks when ordering seek cursors. Date
// alone rounds sub-millisecond ties and can misorder valid adjacent records.
export function notificationInstant(value: unknown): { text: string; ticks: bigint } {
  if (typeof value !== 'string') throw new Error('Invalid notification time');
  const match = /^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?(?:Z|\+00:00)$/.exec(value);
  if (!match) throw new Error('Invalid notification time');
  const milliseconds = Date.parse(`${match[1]}Z`);
  if (!Number.isFinite(milliseconds) || new Date(milliseconds).toISOString().slice(0, 19) !== match[1]) throw new Error('Invalid notification date');
  return { text: value, ticks: BigInt(milliseconds) * 10000n + BigInt((match[2] ?? '').padEnd(7, '0')) };
}
function cursor(value: string) {
  const parts = value.split('/');
  if (parts.length !== 2 || !notificationUuid(parts[1])) throw new Error('Invalid notification cursor');
  return { ticks: notificationInstant(parts[0]).ticks, id: parts[1].toLowerCase() };
}
function before(ticks: bigint, id: string, previous: { ticks: bigint; id: string }) {
  return ticks < previous.ticks || ticks === previous.ticks && id < previous.id;
}
export function isNotificationProfile(value: unknown): value is NotificationProfile {
  const p = value as Record<string, unknown> | null;
  if (!p || !notificationUuid(p.id) || p.status !== 'ACTIVE' || !Number.isSafeInteger(p.version) || (p.version as number) < 1 ||
    typeof p.emailVerified !== 'boolean' || typeof p.locale !== 'string' || p.locale.length > 160 ||
    typeof p.timezone !== 'string' || p.timezone.length > 160) return false;
  try { new Intl.DateTimeFormat(p.locale, { timeZone: p.timezone }).format(); return true; } catch { return false; }
}
export function parseInbox(value: unknown, organizationId: string, recipientId: string, after?: string): InboxPage {
  const p = value as Record<string, unknown> | null;
  if (!p || !notificationUuid(p.organizationId) || p.organizationId.toLowerCase() !== organizationId.toLowerCase() ||
    !Array.isArray(p.items) || p.items.length > 50) throw new Error('Invalid notification scope');
  let previous = after ? cursor(after) : undefined; const ids = new Set<string>();
  const items = p.items.map((value: unknown): InboxItem => {
    const n = value as Record<string, unknown> | null;
    if (!n || !notificationUuid(n.id) || !notificationUuid(n.actorId) || !notificationUuid(n.recipientId) ||
      n.recipientId.toLowerCase() !== recipientId.toLowerCase() ||
      n.actorId.toLowerCase() === recipientId.toLowerCase() && n.type !== 'REMINDER_FIRED' ||
      !notificationType(n.type) || n.entityType !== 'Card' || !notificationUuid(n.entityId) || !notificationUuid(n.boardId)) throw new Error('Invalid notification');
    const currentBoard = n.currentBoardId === undefined ? n.boardId : n.currentBoardId;
    if (!notificationUuid(currentBoard)) throw new Error('Invalid current notification scope');
    const id = n.id.toLowerCase(); const created = notificationInstant(n.createdAt);
    const read = n.readAt === null ? null : notificationInstant(n.readAt);
    const link = `/app/${organizationId.toLowerCase()}/boards/${currentBoard.toLowerCase()}/cards/${n.entityId.toLowerCase()}`;
    if (n.entityLink !== link || ids.has(id) || previous && !before(created.ticks, id, previous) || read && read.ticks < created.ticks) throw new Error('Invalid notification order or link');
    ids.add(id); previous = { ticks: created.ticks, id };
    return { id, type: n.type, actorId: n.actorId.toLowerCase(), recipientId: n.recipientId.toLowerCase(), boardId: n.boardId.toLowerCase(),
      ...(currentBoard.toLowerCase() !== n.boardId.toLowerCase() ? { currentBoardId: currentBoard.toLowerCase() } : {}),
      entityId: n.entityId.toLowerCase(), entityLink: link, createdAt: created.text, createdTicks: created.ticks, readAt: read?.text ?? null };
  });
  if (p.nextCursor !== null) {
    if (typeof p.nextCursor !== 'string' || items.length !== 50) throw new Error('Invalid notification page cursor');
    const next = cursor(p.nextCursor); const last = items.at(-1)!;
    if (next.id !== last.id || next.ticks !== last.createdTicks) throw new Error('Invalid notification page boundary');
  }
  return { items, nextCursor: p.nextCursor as string | null };
}
export function validateReadAcknowledgment(value: unknown, organizationId: string, targets: { id: string; createdTicks: bigint }[]) {
  const p = value as Record<string, unknown> | null;
  if (!p || !notificationUuid(p.organizationId) || p.organizationId.toLowerCase() !== organizationId.toLowerCase() ||
    !Array.isArray(p.items) || p.items.length !== targets.length) throw new Error('Unconfirmed notification read');
  const expected = new Map(targets.map(t => [t.id, t.createdTicks])); const seen = new Set<string>(); let previous: string | undefined;
  for (const value of p.items) {
    const n = value as Record<string, unknown> | null;
    if (!n || !notificationUuid(n.id)) throw new Error('Unconfirmed notification identity');
    const id = n.id.toLowerCase(); const created = expected.get(id);
    if (created === undefined || seen.has(id) || previous && id <= previous || notificationInstant(n.readAt).ticks < created) throw new Error('Unconfirmed notification read');
    seen.add(id); previous = id;
  }
}
