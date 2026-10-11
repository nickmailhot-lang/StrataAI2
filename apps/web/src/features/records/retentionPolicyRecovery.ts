import { WorkInputError } from '../../api/workManagement';
import { notificationUuid } from '../notifications/notificationInbox';
import { RetentionPolicyChangeIntent } from './retentionPolicyClient';

const prefix = 'strataai:retention-policy-change:v1:';
function key(actor: string, organization: string) {
  if (!notificationUuid(actor) || !notificationUuid(organization)) throw new WorkInputError('The retention-policy recovery context is unavailable.');
  return `${prefix}${actor.toLowerCase()}:${organization.toLowerCase()}`;
}
const failure = () => new WorkInputError('This browser could not retain the original retention-policy submission. No new change was sent; retry recovery before approving another change.');
export function restoreRetentionPolicyChange(storage: Storage, actor: string, organization: string) {
  const scope = key(actor, organization);
  try {
    const encoded = storage.getItem(scope); if (encoded === null) return undefined;
    if (encoded.length > 98_304) throw failure();
    const intent = RetentionPolicyChangeIntent.restore(JSON.parse(encoded));
    if (key(intent.actorId, intent.organizationId) !== scope || JSON.stringify(intent.recoveryRecord()) !== encoded) throw failure();
    return intent;
  } catch { throw failure(); }
}
export function retainRetentionPolicyChange(storage: Storage, intent: RetentionPolicyChangeIntent) {
  const scope = key(intent.actorId, intent.organizationId);
  const encoded = JSON.stringify(intent.recoveryRecord());
  const previous = restoreRetentionPolicyChange(storage, intent.actorId, intent.organizationId);
  if (previous && JSON.stringify(previous.recoveryRecord()) !== encoded)
    throw new WorkInputError('Recover the original retention-policy submission before approving another change.');
  try {
    if (!previous) {
      let count = 0;
      for (let index = 0; index < storage.length; index++) if (storage.key(index)?.startsWith(prefix)) count++;
      // Never evict a live original to make room for another Organization.
      if (count >= 32) throw failure();
    }
    storage.setItem(scope, encoded);
    if (storage.getItem(scope) !== encoded) throw failure();
  } catch { throw failure(); }
}
export function completeRetentionPolicyChange(storage: Storage, intent: RetentionPolicyChangeIntent) {
  const scope = key(intent.actorId, intent.organizationId);
  const previous = restoreRetentionPolicyChange(storage, intent.actorId, intent.organizationId);
  if (!previous) return;
  if (JSON.stringify(previous.recoveryRecord()) !== JSON.stringify(intent.recoveryRecord()))
    throw new WorkInputError('Another original retention-policy submission needs recovery.');
  try {
    storage.removeItem(scope);
    if (storage.getItem(scope) !== null) throw failure();
  } catch { throw failure(); }
}
export function forgetRetentionPolicyChanges(actor?: string, organization?: string) {
  try {
    if (actor && organization) { sessionStorage.removeItem(key(actor, organization)); return; }
    for (let index = sessionStorage.length - 1; index >= 0; index--) {
      const candidate = sessionStorage.key(index); if (candidate?.startsWith(prefix)) sessionStorage.removeItem(candidate);
    }
  } catch { /* Account withdrawal/sign-out cannot depend on browser storage. */ }
}
export function forgetForeignRetentionPolicyChanges(storage: Storage, actor: string) {
  key(actor, actor);
  try {
    for (let index = storage.length - 1; index >= 0; index--) {
      const candidate = storage.key(index);
      if (candidate?.startsWith(prefix) && !candidate.startsWith(`${prefix}${actor.toLowerCase()}:`)) storage.removeItem(candidate);
    }
  } catch { throw failure(); }
}
