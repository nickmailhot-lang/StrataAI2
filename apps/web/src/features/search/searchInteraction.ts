import { notificationUuid as uuid } from '../notifications/notificationInbox';

export type SearchInteraction = { eventId: string; eventType: 'SEARCH_EXECUTED'; actorId: string;
  organizationId: null; boardId: null; entityType: 'Search'; entityId: string; version: 1;
  metadata: Record<string, never>; createdAt: string };
export class ChangedSearchInteractionActor extends Error {}
const fields = ['actorId', 'boardId', 'createdAt', 'entityId', 'entityType', 'eventId', 'eventType', 'metadata', 'organizationId', 'version'];

export function parseSearchInteraction(value: unknown, actor: string): SearchInteraction {
  const source = value as Record<string, unknown> | null;
  if (!uuid(actor) || !source || typeof source !== 'object' || Array.isArray(source)
    || Object.keys(source).sort().join(',') !== fields.join(',')
    || !uuid(source.actorId) || !uuid(source.eventId) || !uuid(source.entityId)
    || source.entityId.toLowerCase() !== source.eventId.toLowerCase() || source.eventType !== 'SEARCH_EXECUTED'
    || source.entityType !== 'Search' || source.version !== 1 || source.organizationId !== null || source.boardId !== null
    || !source.metadata || typeof source.metadata !== 'object' || Array.isArray(source.metadata) || Object.keys(source.metadata).length
    || typeof source.createdAt !== 'string' || source.createdAt.length > 40
    || !/^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d{1,6})?(?:Z|\+00:00)$/.test(source.createdAt)
    || !Number.isFinite(Date.parse(source.createdAt))
    || Date.parse(source.createdAt) <= Date.parse('0001-01-01T00:00:00Z')
    || new Date(source.createdAt).toISOString().slice(0, 19) !== source.createdAt.slice(0, 19)) throw new Error('Invalid search interaction');
  if (source.actorId.toLowerCase() !== actor.toLowerCase()) throw new ChangedSearchInteractionActor();
  return { eventId: source.eventId.toLowerCase(), eventType: 'SEARCH_EXECUTED', actorId: actor.toLowerCase(),
    organizationId: null, boardId: null, entityType: 'Search', entityId: source.entityId.toLowerCase(), version: 1,
    metadata: {}, createdAt: source.createdAt };
}

// Bounded process-local acknowledgment window. No criteria/results are cached
// here, and this consumer is separate from optional analytics and durable history.
export class SearchInteractionAcknowledgments {
  private actor: string | undefined;
  private readonly seen = new Map<string, string>();
  clear() { this.actor = undefined; this.seen.clear(); }
  consume(source: SearchInteraction): boolean {
    if (this.actor !== source.actorId) { this.clear(); this.actor = source.actorId; }
    const originalClock = this.seen.get(source.eventId);
    if (originalClock !== undefined) {
      if (originalClock !== source.createdAt) throw new Error('Changed search interaction original');
      return false;
    }
    this.seen.set(source.eventId, source.createdAt);
    if (this.seen.size > 1000) this.seen.delete(this.seen.keys().next().value!);
    return true;
  }
}
