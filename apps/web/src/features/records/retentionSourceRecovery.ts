import { WorkInputError } from '../../api/workManagement';
import { notificationUuid } from '../notifications/notificationInbox';
import { RetentionSourceClassificationIntent } from './retentionSourceClient';

const prefix = 'strataai:retention-source-classification:v1:';
function scope(actor: string, organization: string) {
  if (!notificationUuid(actor) || !notificationUuid(organization)) throw failure();
  return `${prefix}${actor.toLowerCase()}:${organization.toLowerCase()}`;
}
function failure() { return new WorkInputError('The original classification could not be retained. Recover it before confirming another source.'); }
export function restoreRetentionSourceClassification(storage: Storage, actor: string, organization: string) {
  try {
    const key = scope(actor, organization), encoded = storage.getItem(key); if (encoded === null) return undefined;
    if (encoded.length > 98_304) throw failure();
    const intent = RetentionSourceClassificationIntent.restore(JSON.parse(encoded));
    if (scope(intent.actorId, intent.organizationId) !== key || JSON.stringify(intent.recoveryRecord()) !== encoded) throw failure();
    return intent;
  } catch { throw failure(); }
}
export function retainRetentionSourceClassification(storage: Storage, intent: RetentionSourceClassificationIntent) {
  const key = scope(intent.actorId, intent.organizationId), encoded = JSON.stringify(intent.recoveryRecord());
  const previous = restoreRetentionSourceClassification(storage, intent.actorId, intent.organizationId);
  if (previous && JSON.stringify(previous.recoveryRecord()) !== encoded) throw failure();
  try {
    if (!previous) {
      let count = 0;
      for (let index = 0; index < storage.length; index++) if (storage.key(index)?.startsWith(prefix)) count++;
      if (count >= 32) throw failure();
    }
    storage.setItem(key, encoded); if (storage.getItem(key) !== encoded) throw failure();
  } catch { throw failure(); }
}
export function completeRetentionSourceClassification(storage: Storage, intent: RetentionSourceClassificationIntent) {
  const key = scope(intent.actorId, intent.organizationId), previous = restoreRetentionSourceClassification(storage, intent.actorId, intent.organizationId);
  if (!previous) return;
  if (JSON.stringify(previous.recoveryRecord()) !== JSON.stringify(intent.recoveryRecord())) throw failure();
  try { storage.removeItem(key); if (storage.getItem(key) !== null) throw failure(); } catch { throw failure(); }
}
export function forgetRetentionSourceClassifications(actor?: string, organization?: string) {
  try {
    if (actor && organization) { sessionStorage.removeItem(scope(actor, organization)); return; }
    for (let index = sessionStorage.length - 1; index >= 0; index--) {
      const key = sessionStorage.key(index); if (key?.startsWith(prefix)) sessionStorage.removeItem(key);
    }
  } catch { /* Definitive account withdrawal cannot depend on browser storage. */ }
}
export function forgetOrganizationRetentionSourceClassifications(organization: string) {
  if (!notificationUuid(organization)) return;
  const suffix = `:${organization.toLowerCase()}`;
  try {
    for (let index = sessionStorage.length - 1; index >= 0; index--) {
      const key = sessionStorage.key(index);
      if (key?.startsWith(prefix) && key.endsWith(suffix) && notificationUuid(key.slice(prefix.length, -suffix.length))) sessionStorage.removeItem(key);
    }
  } catch { /* Definitive Organization withdrawal cannot depend on browser storage. */ }
}
