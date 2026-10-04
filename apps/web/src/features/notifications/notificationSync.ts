import { notificationInstant, notificationUuid } from './notificationInbox';

const maximum = 9223372036854775807n;
function sequence(value: unknown): bigint | undefined {
  if (typeof value !== 'string' || !/^(0|[1-9][0-9]{0,18})$/.test(value)) return;
  const parsed = BigInt(value);
  return parsed <= maximum ? parsed : undefined;
}
export type NotificationSync = { cursor: string; eventIds: string[]; resetRequired: boolean };

// Events invalidate freshly authorized HTTP content; no event is rendered as inbox content.
export function validateNotificationSync(value: unknown, organizationId: string, recipientId: string,
  after: string | undefined, seen: ReadonlySet<string>): NotificationSync | undefined {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return;
  const data = value as Record<string, unknown>;
  if (!notificationUuid(data.organizationId) || data.organizationId.toLowerCase() !== organizationId.toLowerCase() ||
    !notificationUuid(data.recipientId) || data.recipientId.toLowerCase() !== recipientId.toLowerCase() ||
    typeof data.hasMore !== 'boolean' || typeof data.resetRequired !== 'boolean' ||
    !Array.isArray(data.events) || data.events.length > 50) return;
  const cursor = sequence(data.cursor); const previous = after === undefined ? undefined : sequence(after);
  if (cursor === undefined || after !== undefined && previous === undefined) return;
  if (previous === undefined || data.resetRequired) {
    if (data.events.length || data.hasMore || previous === undefined && data.resetRequired ||
      data.resetRequired && cursor >= previous!) return;
    return { cursor: data.cursor as string, eventIds: [], resetRequired: data.resetRequired };
  }
  if (cursor < previous || cursor - previous > 50n || data.hasMore && cursor - previous !== 50n) return;
  let last = previous; const ids = new Set<string>();
  for (const value of data.events) {
    if (!value || typeof value !== 'object' || Array.isArray(value)) return;
    const event = value as Record<string, unknown>; const next = sequence(event.sequence);
    if (!notificationUuid(event.eventId) || !notificationUuid(event.actorId) || !notificationUuid(event.entityId) ||
      !notificationUuid(event.boardId) || !notificationUuid(event.organizationId) ||
      event.organizationId.toLowerCase() !== organizationId.toLowerCase() || !notificationUuid(event.recipientId) ||
      event.recipientId.toLowerCase() !== recipientId.toLowerCase() || event.entityType !== 'Notification' ||
      next === undefined || next <= last || next > cursor ||
      !['NOTIFICATION_CREATED', 'NOTIFICATION_READ'].includes(event.eventType as string) ||
      event.version !== (event.eventType === 'NOTIFICATION_CREATED' ? 1 : 2) ||
      event.eventType === 'NOTIFICATION_READ' && event.actorId.toLowerCase() !== recipientId.toLowerCase() ||
      !event.metadata || typeof event.metadata !== 'object' || Array.isArray(event.metadata) || Object.keys(event.metadata).length) return;
    const id = event.eventId.toLowerCase();
    if (ids.has(id) || seen.has(id)) return;
    try { notificationInstant(event.createdAt); } catch { return; }
    ids.add(id); last = next;
  }
  return { cursor: data.cursor as string, eventIds: [...ids], resetRequired: false };
}
