import { notificationInstant, notificationUuid } from '../notifications/notificationInbox';

export function validateOrganizationMetadataSync(input: unknown, organizationId: string, userId: string,
  seen: ReadonlyMap<string, string>) {
  try {
    const value = input as Record<string, unknown> | null;
    if (!value || Object.keys(value).sort().join(',') !== 'organizationId,page,userId'
      || value.organizationId !== organizationId || value.userId !== userId
      || !notificationUuid(value.organizationId) || !notificationUuid(value.userId)) return null;
    const p = value.page as Record<string, unknown> | null;
    if (!p || Object.keys(p).sort().join(',') !== 'cursor,events,hasMore,pending,resetRequired'
      || typeof p.cursor !== 'string' || !/^[A-Za-z0-9_-]{1,4096}$/.test(p.cursor) || /^\d+$/.test(p.cursor)
      || typeof p.hasMore !== 'boolean' || typeof p.pending !== 'boolean' || typeof p.resetRequired !== 'boolean'
      || !Array.isArray(p.events) || p.events.length > 50
      || p.hasMore && (p.pending || p.events.length !== 50)
      || p.resetRequired && (p.hasMore || p.pending || p.events.length !== 0)) return null;
    const local = new Set<string>(); const eventIds: [string, string][] = [];
    for (const row of p.events) {
      const e = row as Record<string, unknown> | null;
      if (!e || Object.keys(e).sort().join(',') !== 'actorId,boardId,createdAt,entityId,entityType,eventId,eventType,metadata,organizationId,version'
        || !notificationUuid(e.eventId) || !notificationUuid(e.actorId) || e.organizationId !== organizationId
        || !notificationUuid(e.entityId) || e.boardId !== null
        || (e.eventType === 'ORGANIZATION_MEMBER_INVITED' ? e.entityType !== 'Invitation'
          : e.eventType === 'ORGANIZATION_MEMBER_ADDED' || e.eventType === 'ORGANIZATION_MEMBER_REMOVED' ? e.entityType !== 'OrganizationMembership'
          : e.entityType !== 'Organization' || e.entityId !== organizationId)
        || !Number.isSafeInteger(e.version) || (e.version as number) < 1
        || !['ORGANIZATION_CREATED', 'ORGANIZATION_UPDATED', 'ORGANIZATION_MEMBER_ADDED', 'ORGANIZATION_MEMBER_REMOVED', 'ORGANIZATION_MEMBER_INVITED'].includes(e.eventType as string)
        || (e.eventType === 'ORGANIZATION_CREATED' || e.eventType === 'ORGANIZATION_MEMBER_INVITED') && e.version !== 1
        || (e.eventType === 'ORGANIZATION_UPDATED' || e.eventType === 'ORGANIZATION_MEMBER_REMOVED') && (e.version as number) <= 1
        || !e.metadata || typeof e.metadata !== 'object' || Array.isArray(e.metadata) || Object.keys(e.metadata).length
        || local.has(e.eventId)) return null;
      const fingerprint = `${e.actorId}/${organizationId}/${e.entityType}/${e.entityId}/${e.eventType}/${e.version}/${notificationInstant(e.createdAt).ticks}`;
      local.add(e.eventId);
      const previous = seen.get(e.eventId);
      if (previous !== undefined && previous !== fingerprint) return null;
      if (previous === undefined) eventIds.push([e.eventId, fingerprint]);
    }
    return { cursor: p.cursor, resetRequired: p.resetRequired, eventIds };
  } catch { return null; }
}
