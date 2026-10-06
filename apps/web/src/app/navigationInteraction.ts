import { notificationUuid as uuid } from '../features/notifications/notificationInbox';

export type NavigationTarget =
  | { kind: 'context'; organization: string | null }
  | { kind: 'board'; organization: string; board: string; version: number }
  | { kind: 'card'; organization: string; board: string; card: string; version: number };
export type NavigationInteraction = { eventId: string; eventType: string; actorId: string;
  organizationId: string | null; boardId: string | null; entityType: string; entityId: string;
  version: number; metadata: Record<string, never>; createdAt: string };
export class ChangedNavigationActor extends Error {}
const fields = ['actorId', 'boardId', 'createdAt', 'entityId', 'entityType', 'eventId', 'eventType', 'metadata', 'organizationId', 'version'];
const same = (value: unknown, expected: string | null) => expected === null ? value === null
  : uuid(value) && value.toLowerCase() === expected.toLowerCase();

// Scope comes from independently admitted navigation, never from the response.
export function parseNavigationInteraction(value: unknown, actor: string, target: NavigationTarget): NavigationInteraction {
  const source = value as Record<string, unknown> | null;
  if (!uuid(actor) || !source || typeof source !== 'object' || Array.isArray(source)
    || Object.keys(source).sort().join(',') !== fields.join(',')
    || !uuid(source.eventId) || !uuid(source.actorId) || !uuid(source.entityId)
    || !source.metadata || typeof source.metadata !== 'object' || Array.isArray(source.metadata) || Object.keys(source.metadata).length
    || typeof source.createdAt !== 'string' || source.createdAt.length > 40
    || !/^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d{1,6})?(?:Z|\+00:00)$/.test(source.createdAt)
    || !Number.isFinite(Date.parse(source.createdAt)) || Date.parse(source.createdAt) <= Date.parse('0001-01-01T00:00:00Z')
    || new Date(source.createdAt).toISOString().slice(0, 19) !== source.createdAt.slice(0, 19)) throw new Error('Invalid navigation observation');
  if (!same(source.actorId, actor)) throw new ChangedNavigationActor();
  if (target.organization !== null && !uuid(target.organization)) throw new Error('Invalid navigation target');
  const context = target.kind === 'context';
  const eventType = context ? 'APPLICATION_CONTEXT_CHANGED' : target.kind === 'board' ? 'BOARD_OPENED' : 'CARD_OPENED';
  const entityType = context ? target.organization === null ? 'ApplicationContext' : 'Organization' : target.kind === 'board' ? 'Board' : 'Card';
  const entity = context ? target.organization ?? source.eventId : target.kind === 'board' ? target.board : target.card;
  const version = context ? 1 : target.version;
  if (!context && (!uuid(target.board) || !Number.isSafeInteger(version) || version <= 0)
    || !uuid(entity) || source.eventType !== eventType || source.entityType !== entityType
    || !same(source.organizationId, target.organization) || !same(source.boardId, context ? null : target.board)
    || !same(source.entityId, entity) || source.version !== version) throw new Error('Invalid navigation observation scope');
  return { eventId: source.eventId.toLowerCase(), eventType, actorId: actor.toLowerCase(),
    organizationId: target.organization?.toLowerCase() ?? null, boardId: context ? null : target.board.toLowerCase(),
    entityType, entityId: entity.toLowerCase(), version, metadata: {}, createdAt: source.createdAt };
}

export class NavigationAcknowledgments {
  private actor: string | undefined;
  private readonly seen = new Map<string, string>();
  clear() { this.actor = undefined; this.seen.clear(); }
  consume(source: NavigationInteraction): boolean {
    if (this.actor !== source.actorId) { this.clear(); this.actor = source.actorId; }
    const identity = JSON.stringify(source);
    const original = this.seen.get(source.eventId);
    if (original !== undefined) {
      if (original !== identity) throw new Error('Changed navigation original');
      return false;
    }
    this.seen.set(source.eventId, identity);
    if (this.seen.size > 1000) this.seen.delete(this.seen.keys().next().value!);
    return true;
  }
}
