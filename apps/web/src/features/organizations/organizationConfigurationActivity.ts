import { WorkRequestError } from '../../api/workManagement';
import { notificationInstant, notificationUuid } from '../notifications/notificationInbox';

export type ConfigurationActivityItem = Readonly<{
  eventId: string; actorId: string; actorLabel: string; actingCapacity: ConfigurationActingCapacity;
  version: number; correlationId: string; createdAt: string;
}>;
export type ConfigurationActingCapacity = 'ORGANIZATION_OWNER' | 'ORGANIZATION_ADMIN' | 'UNRECORDED';
export function configurationActingCapacityLabel(capacity: ConfigurationActingCapacity): string {
  return capacity === 'ORGANIZATION_OWNER' ? 'Organization owner'
    : capacity === 'ORGANIZATION_ADMIN' ? 'Organization administrator' : 'Capacity was not recorded';
}
export type ConfigurationActivityPage = Readonly<{
  items: readonly ConfigurationActivityItem[]; nextCursor: string | null;
}>;
export function parseConfigurationActivity(input: unknown, organizationId: string, beforeVersion?: number): ConfigurationActivityPage {
  try {
    const page = input as Record<string, unknown> | null;
    if (!page || Object.keys(page).sort().join(',') !== 'items,nextCursor,organizationId'
      || page.organizationId !== organizationId || !notificationUuid(organizationId)
      || !Array.isArray(page.items) || page.items.length > 50) throw new Error();
    if (beforeVersion !== undefined && (!Number.isSafeInteger(beforeVersion) || beforeVersion < 1)) throw new Error();
    let previous = beforeVersion; const seen = new Set<string>();
    const items = page.items.map(value => {
      const row = value as Record<string, unknown> | null;
      if (!row || Object.keys(row).sort().join(',') !== 'actingCapacity,actorId,actorLabel,boardId,correlationId,createdAt,entityId,entityType,eventId,eventType,metadata,organizationId,version'
        || row.organizationId !== organizationId || row.entityId !== organizationId || row.boardId !== null
        || row.entityType !== 'OrganizationConfiguration' || row.eventType !== 'ORGANIZATION_CONFIGURATION_CHANGED'
        || !notificationUuid(row.eventId) || !notificationUuid(row.actorId) || seen.has(row.eventId.toLowerCase())
        || typeof row.actorLabel !== 'string' || !row.actorLabel.trim() || row.actorLabel.length > 160 || /\p{Cc}/u.test(row.actorLabel)
        || !['ORGANIZATION_OWNER', 'ORGANIZATION_ADMIN', 'UNRECORDED'].includes(row.actingCapacity as string)
        || !Number.isSafeInteger(row.version) || (row.version as number) < 1
        || previous !== undefined && row.version !== previous - 1
        || typeof row.correlationId !== 'string' || !row.correlationId.trim() || row.correlationId.length > 256
        || /\p{Cc}/u.test(row.correlationId) || typeof row.createdAt !== 'string'
        || !row.metadata || typeof row.metadata !== 'object' || Array.isArray(row.metadata) || Object.keys(row.metadata).length !== 0) throw new Error();
      notificationInstant(row.createdAt); previous = row.version as number; seen.add(row.eventId.toLowerCase());
      return Object.freeze({ eventId: row.eventId, actorId: row.actorId, actorLabel: row.actorLabel,
        actingCapacity: row.actingCapacity as ConfigurationActingCapacity, version: previous,
        correlationId: row.correlationId, createdAt: row.createdAt });
    });
    if (page.nextCursor !== null && (typeof page.nextCursor !== 'string'
      || !/^[A-Za-z0-9_-]{1,4096}$/.test(page.nextCursor) || /^\d+$/.test(page.nextCursor) || items.length !== 50)) throw new Error();
    return Object.freeze({ items: Object.freeze(items), nextCursor: page.nextCursor as string | null });
  } catch { throw new WorkRequestError(503, null); }
}
