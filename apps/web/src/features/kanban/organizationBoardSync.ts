import { notificationInstant, notificationUuid } from '../notifications/notificationInbox';

const types = new Set(['BOARD_CREATED', 'BOARD_UPDATED', 'BOARD_COPIED', 'BOARD_ARCHIVED', 'BOARD_RESTORED', 'BOARD_DELETED']);
export function validateOrganizationBoardSync(input: unknown, organizationId: string, userId: string,
  seen: ReadonlyMap<string, string>) {
  try {
    const value = input as Record<string, unknown> | null;
    if (!value || Object.keys(value).sort().join(',') !== 'organizationId,page,userId' ||
      value.organizationId !== organizationId || value.userId !== userId ||
      !notificationUuid(value.organizationId) || !notificationUuid(value.userId)) return null;
    const p = value.page as Record<string, unknown> | null;
    if (!p || Object.keys(p).sort().join(',') !== 'cursor,events,hasMore,pending,resetRequired' ||
      typeof p.cursor !== 'string' || !/^[A-Za-z0-9_-]{1,4096}$/.test(p.cursor) || /^\d+$/.test(p.cursor) ||
      typeof p.hasMore !== 'boolean' || typeof p.pending !== 'boolean' || typeof p.resetRequired !== 'boolean' ||
      !Array.isArray(p.events) || p.events.length > 50 || p.hasMore && (p.pending || p.events.length !== 50) ||
      p.resetRequired && (p.hasMore || p.pending || p.events.length !== 0)) return null;
    const local = new Set<string>(); const eventIds: [string, string][] = [];
    for (const row of p.events) {
      const e = row as Record<string, unknown> | null;
      if (!e || Object.keys(e).sort().join(',') !== 'boardId,createdAt,eventId,eventType,version' ||
        !notificationUuid(e.eventId) || !notificationUuid(e.boardId) || typeof e.eventType !== 'string' || !types.has(e.eventType) ||
        !Number.isSafeInteger(e.version) || (e.version as number) < 1 || local.has(e.eventId)) return null;
      const fingerprint = `${e.boardId}/${e.eventType}/${e.version}/${notificationInstant(e.createdAt).ticks}`;
      local.add(e.eventId);
      const previous = seen.get(e.eventId);
      if (previous !== undefined && previous !== fingerprint) return null;
      if (previous === undefined) eventIds.push([e.eventId, fingerprint]);
    }
    return { cursor: p.cursor, resetRequired: p.resetRequired, eventIds };
  } catch { return null; }
}
