import { boundedWorkRead, workRequest } from '../api/workManagement';
import { notificationUuid as uuid } from '../features/notifications/notificationInbox';
import { ChangedNavigationActor, NavigationAcknowledgments, parseNavigationInteraction, type NavigationTarget } from './navigationInteraction';

export type NavigationIntent = Readonly<{ actor: string; key: string; target: Readonly<NavigationTarget>; createdAt: number }>;
const lifetime = 24 * 60 * 60 * 1000;
function targetQuery(target: NavigationTarget): string {
  if (!target || typeof target !== 'object' || !['context', 'board', 'card'].includes(target.kind)
    || target.organization !== null && !uuid(target.organization)) throw new Error('Invalid navigation target');
  const fields = target.kind === 'context' ? ['kind', 'organization']
    : target.kind === 'board' ? ['board', 'kind', 'organization', 'version'] : ['board', 'card', 'kind', 'organization', 'version'];
  if (Object.keys(target).sort().join(',') !== fields.join(',')) throw new Error('Invalid navigation target');
  const query = new URLSearchParams({ kind: target.kind });
  if (target.organization !== null) query.set('organizationId', target.organization.toLowerCase());
  if (target.kind !== 'context') {
    if (!uuid(target.organization) || !uuid(target.board) || !Number.isSafeInteger(target.version) || target.version <= 0
      || target.kind === 'card' && !uuid(target.card)) throw new Error('Invalid navigation target');
    query.set('boardId', target.board.toLowerCase());
    if (target.kind === 'card') query.set('cardId', target.card.toLowerCase());
    query.set('version', String(target.version));
  }
  return query.toString();
}
export function validateNavigationTarget(target: NavigationTarget): void { targetQuery(target); }
export function createNavigationIntent(actor: string, target: NavigationTarget, now = Date.now()): NavigationIntent {
  if (!uuid(actor) || !Number.isSafeInteger(now) || now < 0) throw new Error('Invalid navigation account or clock');
  targetQuery(target);
  const normalized = target.kind === 'context' ? { kind: target.kind, organization: target.organization?.toLowerCase() ?? null }
    : target.kind === 'board' ? { kind: target.kind, organization: target.organization.toLowerCase(), board: target.board.toLowerCase(), version: target.version }
      : { kind: target.kind, organization: target.organization.toLowerCase(), board: target.board.toLowerCase(), card: target.card.toLowerCase(), version: target.version };
  return Object.freeze({ actor: actor.toLowerCase(), key: crypto.randomUUID(), target: Object.freeze(normalized), createdAt: now });
}
export function validateNavigationIntent(intent: NavigationIntent, now = Date.now()): void {
  if (!intent || Object.keys(intent).sort().join(',') !== 'actor,createdAt,key,target' || !uuid(intent.actor) || !uuid(intent.key)
    || !Number.isSafeInteger(now) || !Number.isSafeInteger(intent.createdAt) || intent.createdAt < 0
    || now < intent.createdAt || now - intent.createdAt >= lifetime) throw new Error('Invalid or expired navigation intent');
  targetQuery(intent.target);
}
export async function submitNavigationIntent(intent: NavigationIntent, signal: AbortSignal,
  acknowledgments: NavigationAcknowledgments, now = Date.now()) {
  validateNavigationIntent(intent, now);
  const query = targetQuery(intent.target);
  async function verifyActor(currentSignal: AbortSignal) {
    const profile = await workRequest<{ id: unknown }>('/me', { signal: currentSignal });
    if (!uuid(profile?.id) || profile.id.toLowerCase() !== intent.actor) throw new ChangedNavigationActor();
  }
  return boundedWorkRead(async currentSignal => {
    await verifyActor(currentSignal);
    const value = await workRequest<unknown>(`/navigation/observations?${query}`, { method: 'POST', signal: currentSignal,
      headers: { 'Idempotency-Key': intent.key, 'X-StrataAI-Expected-Actor': intent.actor } });
    await verifyActor(currentSignal); currentSignal.throwIfAborted(); signal.throwIfAborted();
    const source = parseNavigationInteraction(value, intent.actor, intent.target);
    return { source, firstAcknowledgment: acknowledgments.consume(source) };
  }, signal);
}
