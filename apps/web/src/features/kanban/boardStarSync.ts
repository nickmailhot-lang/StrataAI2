import { notificationInstant, notificationUuid } from '../notifications/notificationInbox';
const decimal = (value: unknown): value is string => typeof value === 'string' && /^[0-9]{1,19}$/.test(value) && BigInt(value) <= 9223372036854775807n;
export function validateBoardStarSync(input: unknown, org: string, board: string, user: string,
  cursor: string | undefined, seen: ReadonlyMap<string,string>) {
  try {
    const p = input as Record<string,unknown> | null;
    if (!p || Object.keys(p).sort().join(',') !== 'boardId,cursor,events,hasMore,organizationId,resetRequired,userId'
      || p.organizationId !== org || p.boardId !== board || p.userId !== user || !decimal(p.cursor)
      || typeof p.hasMore !== 'boolean' || typeof p.resetRequired !== 'boolean' || !Array.isArray(p.events) || p.events.length > 50) return null;
    const end = BigInt(p.cursor), before = cursor === undefined ? undefined : BigInt(cursor);
    if (p.resetRequired ? before === undefined || end >= before : before !== undefined && end < before) return null;
    // A transition can commit between the server's initial head and event reads.
    if (p.hasMore && p.events.length !== 50) return null;
    const eventIds: [string,string][] = []; const local = new Set<string>(); let previous = 0n;
    let entity: string | undefined;
    for (const value of p.events) {
      const e = value as Record<string,unknown> | null;
      if (!e || Object.keys(e).sort().join(',') !== 'actorId,boardId,createdAt,entityId,entityType,eventId,eventType,metadata,organizationId,version'
        || e.actorId !== user || e.organizationId !== org || e.boardId !== board || e.eventType !== 'BOARD_STARRED'
        || e.entityType !== 'UserBoardPreference' || !notificationUuid(e.eventId) || !notificationUuid(e.entityId)
        || !Number.isSafeInteger(e.version) || (e.version as number) < 1 || !e.metadata || typeof e.metadata !== 'object'
        || Array.isArray(e.metadata) || Object.keys(e.metadata).length) return null;
      const revision = BigInt(e.version as number); const ticks = notificationInstant(e.createdAt).ticks;
      if (revision <= previous || revision > end || local.has(e.eventId) || entity && entity !== e.entityId) return null;
      previous = revision; entity = e.entityId; local.add(e.eventId);
      const fingerprint = e.entityId + '/' + revision + '/' + ticks;
      if (!p.resetRequired && before !== undefined && revision <= before) {
        if (seen.get(e.eventId) !== fingerprint) return null;
      } else {
        if (!p.resetRequired && seen.has(e.eventId)) return null;
        eventIds.push([e.eventId,fingerprint]);
      }
    }
    if (p.hasMore && previous !== end || before !== undefined && !p.resetRequired && p.events.length === 0 && end !== before) return null;
    return { cursor: p.cursor, resetRequired: p.resetRequired, eventIds };
  } catch { return null; }
}
