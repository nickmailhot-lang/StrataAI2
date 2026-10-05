import { notificationUuid as uuid } from '../notifications/notificationInbox';

type InteractionOriginal = { eventId: string; actorId: string; entityId: string; version: 1;
  metadata: Record<string, never>; createdAt: string };
export type SearchInteraction = InteractionOriginal & { eventType: 'SEARCH_EXECUTED';
  organizationId: null; boardId: null; entityType: 'Search' };
export type BoardFilterInteraction = InteractionOriginal & { eventType: 'BOARD_FILTER_CHANGED';
  organizationId: string; boardId: string; entityType: 'BoardFilter' };
export type PrivateSearchInteraction = SearchInteraction | BoardFilterInteraction;
export class ChangedSearchInteractionActor extends Error {}
const fields = ['actorId', 'boardId', 'createdAt', 'entityId', 'entityType', 'eventId', 'eventType', 'metadata', 'organizationId', 'version'];

function parseInteractionOriginal(value: unknown, actor: string): Record<string, unknown> & InteractionOriginal {
  const source = value as Record<string, unknown> | null;
  if (!uuid(actor) || !source || typeof source !== 'object' || Array.isArray(source)
    || Object.keys(source).sort().join(',') !== fields.join(',')
    || !uuid(source.actorId) || !uuid(source.eventId) || !uuid(source.entityId)
    || source.entityId.toLowerCase() !== source.eventId.toLowerCase() || source.version !== 1
    || !source.metadata || typeof source.metadata !== 'object' || Array.isArray(source.metadata) || Object.keys(source.metadata).length
    || typeof source.createdAt !== 'string' || source.createdAt.length > 40
    || !/^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d{1,6})?(?:Z|\+00:00)$/.test(source.createdAt)
    || !Number.isFinite(Date.parse(source.createdAt))
    || Date.parse(source.createdAt) <= Date.parse('0001-01-01T00:00:00Z')
    || new Date(source.createdAt).toISOString().slice(0, 19) !== source.createdAt.slice(0, 19)) throw new Error('Invalid search interaction');
  if (source.actorId.toLowerCase() !== actor.toLowerCase()) throw new ChangedSearchInteractionActor();
  return { ...source, eventId: source.eventId.toLowerCase(), actorId: actor.toLowerCase(),
    entityId: source.entityId.toLowerCase(), version: 1, metadata: {}, createdAt: source.createdAt };
}

export function parseSearchInteraction(value: unknown, actor: string): SearchInteraction {
  const source = parseInteractionOriginal(value, actor);
  if (source.eventType !== 'SEARCH_EXECUTED' || source.entityType !== 'Search'
    || source.organizationId !== null || source.boardId !== null) throw new Error('Invalid search interaction scope');
  return { eventId: source.eventId, eventType: 'SEARCH_EXECUTED', actorId: source.actorId,
    organizationId: null, boardId: null, entityType: 'Search', entityId: source.entityId.toLowerCase(), version: 1,
    metadata: {}, createdAt: source.createdAt };
}

// Called against the current admitted Board scope, never a scope taken from the
// acknowledgment itself. Anonymous filtering has no personal acknowledgment.
export function parseBoardFilterInteraction(value: unknown, actor: string, organization: string, board: string): BoardFilterInteraction {
  const source = parseInteractionOriginal(value, actor);
  if (!uuid(organization) || !uuid(board) || source.eventType !== 'BOARD_FILTER_CHANGED' || source.entityType !== 'BoardFilter'
    || !uuid(source.organizationId) || !uuid(source.boardId)
    || source.organizationId.toLowerCase() !== organization.toLowerCase() || source.boardId.toLowerCase() !== board.toLowerCase())
    throw new Error('Invalid Board filter interaction scope');
  return { eventId: source.eventId, eventType: 'BOARD_FILTER_CHANGED', actorId: source.actorId,
    organizationId: organization.toLowerCase(), boardId: board.toLowerCase(), entityType: 'BoardFilter',
    entityId: source.entityId, version: 1, metadata: {}, createdAt: source.createdAt };
}

// Bounded process-local acknowledgment window. No criteria/results are cached
// here, and this consumer is separate from optional analytics and durable history.
export class SearchInteractionAcknowledgments {
  private actor: string | undefined;
  private readonly seen = new Map<string, string>();
  clear() { this.actor = undefined; this.seen.clear(); }
  consume(source: PrivateSearchInteraction): boolean {
    if (this.actor !== source.actorId) { this.clear(); this.actor = source.actorId; }
    const identity = JSON.stringify([source.eventType, source.actorId, source.organizationId, source.boardId,
      source.entityType, source.entityId, source.version, source.createdAt]);
    const original = this.seen.get(source.eventId);
    if (original !== undefined) {
      if (original !== identity) throw new Error('Changed search interaction original');
      return false;
    }
    this.seen.set(source.eventId, identity);
    if (this.seen.size > 1000) this.seen.delete(this.seen.keys().next().value!);
    return true;
  }
}
