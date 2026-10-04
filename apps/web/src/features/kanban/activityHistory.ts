import { notificationUuid } from '../notifications/notificationInbox';
import { dateInstantTicks } from './cardDates';

export type ActivityScope = { organizationId: string; boardId: string; kind: 'BOARD' | 'CARD'; targetId: string };
export type ActivityItem = { eventId: string; organizationId: string; boardId: string; actorId: string; actorLabel: string;
  eventType: string; entityType: string; entityId: string; version: string; createdAt: string; metadata: Record<string, never>; currentBoardId: string };
export type ActivityPage = { organizationId: string; kind: 'BOARD' | 'CARD'; targetId: string; items: ActivityItem[]; nextCursor: string | null };
const invalid = () => new Error('Invalid activity response');
const same = (value: unknown, expected: string) => notificationUuid(value) && value.toLowerCase() === expected.toLowerCase();
function object(value: unknown, keys: string[]) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw invalid();
  const row = value as Record<string, unknown>;
  if (Object.keys(row).length !== keys.length || keys.some(key => !Object.hasOwn(row, key))) throw invalid();
  return row;
}
function time(value: unknown) {
  if (typeof value !== 'string' || !/(?:Z|\+00:00)$/.test(value)) throw invalid();
  const ticks = dateInstantTicks(value) + 621355968000000000n;
  if (ticks < 0n || ticks > 3155378975999999999n || ticks % 10n) throw invalid();
  return ticks;
}
export function parseActivityPage(value: unknown, scope: ActivityScope, before?: ActivityItem): ActivityPage {
  const page = object(value, ['organizationId', 'kind', 'targetId', 'items', 'nextCursor']);
  if (!same(page.organizationId, scope.organizationId) || page.kind !== scope.kind || !same(page.targetId, scope.targetId)
    || !notificationUuid(scope.boardId) || !Array.isArray(page.items) || page.items.length > 50) throw invalid();
  if (page.nextCursor !== null && (typeof page.nextCursor !== 'string' || page.nextCursor.length < 1 || page.nextCursor.length > 2048
    || !/^[A-Za-z0-9_-]+$/.test(page.nextCursor) || page.items.length !== 50)) throw invalid();
  const identities = new Set<string>(); let previous = before;
  for (const value of page.items) {
    const row = object(value, ['eventId', 'organizationId', 'boardId', 'actorId', 'actorLabel', 'eventType', 'entityType', 'entityId', 'version', 'createdAt', 'metadata', 'currentBoardId']);
    if (!same(row.organizationId, scope.organizationId) || !notificationUuid(row.eventId) || !notificationUuid(row.boardId)
      || !notificationUuid(row.actorId) || !notificationUuid(row.entityId) || !notificationUuid(row.currentBoardId)
      || (scope.kind === 'BOARD' && !same(row.boardId, scope.targetId))
      || (scope.kind === 'CARD' && (!same(row.currentBoardId, scope.boardId) || row.entityType === 'Card' && !same(row.entityId, scope.targetId)))
      || typeof row.actorLabel !== 'string' || [...row.actorLabel].length < 1 || [...row.actorLabel].length > 160
      || /[\p{Cc}\p{Cs}]/u.test(row.actorLabel) || typeof row.eventType !== 'string' || !/^[A-Z][A-Z0-9_]{0,79}$/.test(row.eventType)
      || !['Board', 'List', 'Card', 'Label', 'WatchSubscription', 'Reminder'].includes(String(row.entityType))
      || scope.kind === 'CARD' && !['Card', 'WatchSubscription', 'Reminder'].includes(String(row.entityType))
      || typeof row.version !== 'string' || !/^[1-9][0-9]{0,18}$/.test(row.version) || BigInt(row.version) > 9223372036854775807n) throw invalid();
    object(row.metadata, []);
    const id = row.eventId.toLowerCase(); if (identities.has(id)) throw invalid(); identities.add(id);
    const at = time(row.createdAt);
    if (previous && (at > time(previous.createdAt) || at === time(previous.createdAt) && id >= previous.eventId.toLowerCase())) throw invalid();
    previous = row as ActivityItem;
  }
  return page as ActivityPage;
}
const labels: Record<string, string> = {
  BOARD_CREATED: 'created the Board', BOARD_UPDATED: 'updated the Board', BOARD_ARCHIVED: 'archived the Board', BOARD_RESTORED: 'restored the Board',
  LIST_CREATED: 'created a List', LIST_UPDATED: 'updated a List', LIST_MOVED: 'moved a List', LIST_ARCHIVED: 'archived a List', LIST_RESTORED: 'restored a List', LIST_DELETED: 'deleted a List',
  CARD_CREATED: 'created a Card', CARD_COPIED: 'copied a Card', CARD_UPDATED: 'updated a Card', CARD_MOVED: 'moved a Card', CARD_ARCHIVED: 'archived a Card', CARD_RESTORED: 'restored a Card', CARD_DELETED: 'deleted a Card',
  COMMENT_ADDED: 'added a comment', COMMENT_EDITED: 'edited a comment', COMMENT_DELETED: 'removed a comment body', MENTION_CREATED: 'created comment mentions',
  WATCH_CREATED: 'started watching an item', WATCH_REMOVED: 'stopped watching an item', REMINDER_SCHEDULED: 'scheduled a personal reminder', REMINDER_CANCELLED: 'cancelled a personal reminder', REMINDER_FIRED: 'received a personal reminder',
  CARD_MEMBER_ADDED: 'assigned a Card member', CARD_MEMBER_REMOVED: 'removed a Card member', CARD_DATE_CHANGED: 'changed Card dates', CARD_DUE_COMPLETED: 'completed a due date', CARD_DUE_REOPENED: 'reopened a due date',
};
export function activityLabel(item: ActivityItem): string {
  return labels[item.eventType] ?? item.eventType.toLowerCase().replaceAll('_', ' ');
}
export function activityCardLink(item: ActivityItem): string | undefined {
  return item.entityType === 'Card' ? `/app/${item.organizationId}/boards/${item.currentBoardId}/cards/${item.entityId}` : undefined;
}
