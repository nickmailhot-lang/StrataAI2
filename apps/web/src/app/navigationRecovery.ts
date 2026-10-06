import { notificationUuid as uuid } from '../features/notifications/notificationInbox';
import type { NavigationTarget } from './navigationInteraction';
import { validateNavigationIntent, validateNavigationTarget, type NavigationIntent } from './navigationObservation';

const prefix = 'strataai:navigation:v1:';
function storageKey(actor: string, target: NavigationTarget): string {
  if (!uuid(actor)) throw new Error('Invalid navigation account');
  validateNavigationTarget(target);
  return `${prefix}${actor.toLowerCase()}:${target.kind}:${target.organization?.toLowerCase() ?? 'global'}:${target.kind === 'context' ? '' : target.board.toLowerCase()}:${target.kind === 'card' ? target.card.toLowerCase() : ''}`;
}
export function restoreNavigationIntent(storage: Storage, actor: string, target: NavigationTarget, now = Date.now()): NavigationIntent | undefined {
  const key = storageKey(actor, target);
  const encoded = storage.getItem(key);
  if (encoded === null) return undefined;
  try {
    const value = JSON.parse(encoded) as NavigationIntent;
    validateNavigationIntent(value, now);
    if (storageKey(value.actor, value.target) !== key) throw new Error('Changed navigation recovery scope');
    return Object.freeze({ ...value, target: Object.freeze({ ...value.target }) });
  } catch {
    storage.removeItem(key);
    return undefined;
  }
}
export function retainNavigationIntent(storage: Storage, intent: NavigationIntent, now = Date.now()): void {
  validateNavigationIntent(intent, now);
  const key = storageKey(intent.actor, intent.target);
  const original = restoreNavigationIntent(storage, intent.actor, intent.target, now);
  if (original && JSON.stringify(original) !== JSON.stringify(intent)) throw new Error('Recover the original navigation first');
  if (!original) {
    const keys = Object.keys(storage).filter(candidate => candidate.startsWith(prefix));
    let removed = 0;
    if (keys.length >= 1000) {
      // Reclaim at most 100 canonical expired originals for this account.
      // Foreign, malformed and still-recoverable records remain untouched.
      for (const candidate of keys.filter(candidate => candidate.startsWith(`${prefix}${intent.actor.toLowerCase()}:`)).slice(0, 1000)) {
        if (removed >= 100) break;
        try {
          const value = JSON.parse(storage.getItem(candidate) ?? 'null') as NavigationIntent;
          validateNavigationIntent(value, value.createdAt);
          if (value.actor !== intent.actor || storageKey(value.actor, value.target) !== candidate || now - value.createdAt < 86400000) continue;
          storage.removeItem(candidate); removed++;
        } catch { /* A malformed record cannot authorize eviction. */ }
      }
    }
    if (keys.length - removed >= 1000) throw new Error('Navigation recovery storage is full');
  }
  storage.setItem(key, JSON.stringify(intent));
}
export function completeNavigationIntent(storage: Storage, intent: NavigationIntent): void {
  const key = storageKey(intent.actor, intent.target);
  const encoded = storage.getItem(key);
  if (encoded !== null && JSON.parse(encoded).key === intent.key) storage.removeItem(key);
}
