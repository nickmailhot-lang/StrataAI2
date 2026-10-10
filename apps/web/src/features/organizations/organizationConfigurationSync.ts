import { notificationInstant, notificationUuid } from '../notifications/notificationInbox';

// Configuration references have their own admin-only stream and revision order.
// Never accept general Organization metadata or private configuration fields.
export function validateOrganizationConfigurationSync(input: unknown, organizationId: string, userId: string,
  seen: ReadonlyMap<string, string>) {
  try {
    const value = input as Record<string, unknown> | null;
    if (!value || Object.keys(value).sort().join(',') !== 'organizationId,page,userId'
      || value.organizationId !== organizationId || value.userId !== userId
      || !notificationUuid(value.organizationId) || !notificationUuid(value.userId)) return null;
    const page = value.page as Record<string, unknown> | null;
    if (!page || Object.keys(page).sort().join(',') !== 'cursor,events,hasMore,pending,resetRequired'
      || typeof page.cursor !== 'string' || !/^[A-Za-z0-9_-]{1,4096}$/.test(page.cursor) || /^\d+$/.test(page.cursor)
      || typeof page.hasMore !== 'boolean' || typeof page.pending !== 'boolean' || typeof page.resetRequired !== 'boolean'
      || !Array.isArray(page.events) || page.events.length > 50
      || page.hasMore && (page.pending || page.events.length !== 50)
      || page.resetRequired && (page.hasMore || page.pending || page.events.length !== 0)) return null;
    const local = new Set<string>(); const eventIds: [string, string][] = []; let previous = 0;
    for (const row of page.events) {
      const event = row as Record<string, unknown> | null;
      if (!event || Object.keys(event).sort().join(',') !== 'actorId,correlationId,createdAt,entityId,entityType,eventId,eventType,organizationId,version'
        || !notificationUuid(event.eventId) || !notificationUuid(event.actorId) || event.organizationId !== organizationId
        || event.entityId !== organizationId || event.entityType !== 'OrganizationConfiguration'
        || event.eventType !== 'ORGANIZATION_CONFIGURATION_CHANGED' || !Number.isSafeInteger(event.version)
        || (event.version as number) < 1 || previous && event.version !== previous + 1
        || typeof event.correlationId !== 'string' || !event.correlationId.trim() || event.correlationId.length > 256
        || /\p{Cc}/u.test(event.correlationId) || local.has(event.eventId)) return null;
      const fingerprint = JSON.stringify([event.actorId, organizationId, event.entityType, event.entityId,
        event.eventType, event.version, event.correlationId, notificationInstant(event.createdAt).ticks.toString()]);
      previous = event.version as number; local.add(event.eventId);
      const original = seen.get(event.eventId);
      if (original !== undefined && original !== fingerprint) return null;
      if (original === undefined) eventIds.push([event.eventId, fingerprint]);
    }
    return { cursor: page.cursor, resetRequired: page.resetRequired, eventIds, version: previous || undefined };
  } catch { return null; }
}
