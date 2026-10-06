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
  if (!original && Object.keys(storage).filter(candidate => candidate.startsWith(prefix)).length >= 1000)
    throw new Error('Navigation recovery storage is full');
  storage.setItem(key, JSON.stringify(intent));
}
export function completeNavigationIntent(storage: Storage, intent: NavigationIntent): void {
  const key = storageKey(intent.actor, intent.target);
  const encoded = storage.getItem(key);
  if (encoded !== null && JSON.parse(encoded).key === intent.key) storage.removeItem(key);
}
