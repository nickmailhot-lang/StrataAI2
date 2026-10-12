import { apiFetch } from '../../api/apiFetch';
import { boundedWorkRead, WorkInputError, WorkRequestError } from '../../api/workManagement';
import { notificationInstant, notificationUuid } from '../notifications/notificationInbox';
import { readRetentionPolicies, type RetentionPolicy } from './retentionPolicyClient';
import { parseRetentionSourceReview, type RetentionSourceKind, type RetentionSourceReview } from './retentionSourceClient';

export type HoldClass = RetentionPolicy['recordClass'];
export type RetentionHold = { id: string; organizationId: string; version: number; recordClass: HoldClass | null;
  category: string | null; recordId: string | null; status: 'active' | 'released'; reason: string;
  appliedBy: string; appliedAt: string; releasedBy: string | null; releasedAt: string | null };
export type HoldApplication = Pick<RetentionHold, 'recordClass' | 'category' | 'recordId' | 'reason'> & { review: RetentionSourceReview | null; confirmed: true };
export type HoldIntent = { actorId: string; organizationId: string; holdId: string; key: string;
  operation: 'apply' | 'release'; body: HoldApplication | { version: number; confirmed: true } };
const admissionCursor = 'ffffffff-ffff-ffff-ffff-ffffffffffff';
const classes = ['operationalWork', 'governance', 'audit', 'correspondence', 'document', 'ai'];
function unavailable(): never { throw new WorkRequestError(503, null); }
function identity(value: string) { if (!notificationUuid(value)) throw new WorkInputError('The hold context is unavailable.'); return value.toLowerCase(); }
function scope(recordClass: unknown, category: unknown, recordId: unknown) {
  return (recordClass === null || classes.includes(recordClass as string))
    && (category === null || typeof category === 'string' && /^[A-Za-z0-9_.-]{1,64}$/.test(category))
    && (recordId === null || notificationUuid(recordId))
    && (category === null && recordId === null || recordClass !== null);
}
export function parseHold(value: unknown, organization: string, id: string): RetentionHold {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return unavailable();
  const row = value as RetentionHold;
  if (Object.keys(row).sort().join(',') !== ['id','organizationId','version','recordClass','category','recordId','status','reason','appliedBy','appliedAt','releasedBy','releasedAt'].sort().join(',')
    || row.id !== id || row.organizationId !== organization || !Number.isSafeInteger(row.version) || row.version < 1
    || !scope(row.recordClass, row.category, row.recordId) || typeof row.reason !== 'string' || !row.reason.trim()
    || row.reason.length > 2000 || row.reason.trim() !== row.reason || !notificationUuid(row.appliedBy) || !['active','released'].includes(row.status)) return unavailable();
  try {
    const applied = notificationInstant(row.appliedAt);
    if (row.status === 'active' ? row.releasedBy !== null || row.releasedAt !== null
      : row.version < 2 || !notificationUuid(row.releasedBy) || row.releasedAt === null || notificationInstant(row.releasedAt).ticks < applied.ticks) return unavailable();
  } catch { return unavailable(); }
  return row;
}
const path = (organization: string, id: string) => `/organizations/${identity(organization)}/retention-holds/${identity(id)}`;
export function readHold(organization: string, id: string, actor: string, signal: AbortSignal) {
  return boundedWorkRead(async bounded => {
    await readRetentionPolicies(organization, bounded, actor, admissionCursor);
    const response = await apiFetch(path(organization,id), { signal: bounded, headers: { 'X-StrataAI-Expected-Actor': actor } });
    if (!response.ok) throw new WorkRequestError(response.status, null);
    const hold = parseHold(await response.json(), organization, id); bounded.throwIfAborted();
    await readRetentionPolicies(organization, bounded, actor, admissionCursor); return hold;
  }, signal);
}
export function reviewHoldSource(organization: string, actor: string, kind: RetentionSourceKind, id: string, signal: AbortSignal) {
  return boundedWorkRead(async bounded => {
    await readRetentionPolicies(organization, bounded, actor, admissionCursor);
    const response = await apiFetch(`/organizations/${identity(organization)}/retention-holds/sources/${kind}/${identity(id)}/review`,
      { signal: bounded, headers: { 'X-StrataAI-Expected-Actor': actor } });
    if (!response.ok) throw new WorkRequestError(response.status, null);
    const review = parseRetentionSourceReview(await response.json(), organization, kind, id); bounded.throwIfAborted();
    await readRetentionPolicies(organization, bounded, actor, admissionCursor); return review;
  }, signal);
}
export function validateHoldIntent(value: unknown, actor: string, organization: string): HoldIntent {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new WorkInputError('Original hold recovery is unavailable.');
  const intent = value as HoldIntent;
  if (Object.keys(intent).sort().join(',') !== ['actorId','organizationId','holdId','key','operation','body'].sort().join(',')
    || intent.actorId !== identity(actor) || intent.organizationId !== identity(organization)) throw new WorkInputError('Original hold recovery is unavailable.');
  identity(intent.holdId); identity(intent.key);
  if (!intent.body || typeof intent.body !== 'object' || intent.body.confirmed !== true) throw new WorkInputError('Explicit confirmation is required.');
  if (intent.operation === 'release') {
    if (Object.keys(intent.body).sort().join(',') !== 'confirmed,version' || !('version' in intent.body)
      || !Number.isSafeInteger(intent.body.version) || intent.body.version < 1) throw new WorkInputError('Review the current hold version.');
  } else if (intent.operation === 'apply') {
    const body = intent.body as HoldApplication;
    if (Object.keys(body).sort().join(',') !== 'category,confirmed,reason,recordClass,recordId,review'
      || !scope(body.recordClass, body.category, body.recordId) || typeof body.reason !== 'string' || !body.reason.trim()
      || body.reason.trim() !== body.reason || body.reason.length > 2000) throw new WorkInputError('Provide a valid hold scope and reason.');
    if (body.recordId === null ? body.review !== null : !body.review) throw new WorkInputError('Review the classified source.');
    if (body.review) {
      const review = parseRetentionSourceReview(body.review, organization, body.review.kind, body.recordId!);
      if (body.recordClass !== (review.kind === 'attachment' ? 'document' : 'operationalWork')) throw new WorkInputError('The source class must match the review.');
    }
  } else throw new WorkInputError('Original hold recovery is unavailable.');
  return JSON.parse(JSON.stringify(intent)) as HoldIntent;
}
const storageKey = (actor: string, organization: string) => `strataai:hold-command:v1:${identity(actor)}:${identity(organization)}`;
export function restoreHoldIntent(actor: string, organization: string) {
  const encoded = sessionStorage.getItem(storageKey(actor, organization));
  if (encoded === null) return undefined;
  if (encoded.length > 32768) throw new WorkInputError('Original hold recovery is unavailable.');
  return validateHoldIntent(JSON.parse(encoded), actor, organization);
}
export function retainHoldIntent(intent: HoldIntent) {
  const valid = validateHoldIntent(intent, intent.actorId, intent.organizationId), key = storageKey(intent.actorId,intent.organizationId);
  const previous = sessionStorage.getItem(key), encoded = JSON.stringify(valid);
  if (previous !== null && previous !== encoded) throw new WorkInputError('Recover the original hold submission first.');
  sessionStorage.setItem(key, encoded);
  if (sessionStorage.getItem(key) !== encoded) throw new WorkInputError('Original hold recovery could not be retained.');
}
export function forgetHoldIntent(actor: string, organization: string) { sessionStorage.removeItem(storageKey(actor,organization)); }
export function submitHoldIntent(original: HoldIntent, signal: AbortSignal) {
  const intent = validateHoldIntent(original, original.actorId, original.organizationId);
  return boundedWorkRead(async bounded => {
    await readRetentionPolicies(intent.organizationId, bounded, intent.actorId, admissionCursor); bounded.throwIfAborted();
    const response = await apiFetch(`${path(intent.organizationId,intent.holdId)}/${intent.operation}`, { method: 'POST', signal: bounded,
      headers: { 'Content-Type': 'application/json', 'Idempotency-Key': intent.key, 'X-StrataAI-Expected-Actor': intent.actorId }, body: JSON.stringify(intent.body) });
    if (!response.ok) throw new WorkRequestError(response.status, null);
    const hold = parseHold(await response.json(),intent.organizationId,intent.holdId); bounded.throwIfAborted();
    if (intent.operation === 'apply') {
      const body = intent.body as HoldApplication;
      if (hold.version !== 1 || hold.status !== 'active' || hold.appliedBy !== intent.actorId
        || (['recordClass','category','recordId','reason'] as const).some(key => hold[key] !== body[key])) return unavailable();
    } else if (hold.version !== (intent.body as {version:number}).version + 1 || hold.status !== 'released' || hold.releasedBy !== intent.actorId) return unavailable();
    await readRetentionPolicies(intent.organizationId, bounded, intent.actorId, admissionCursor); return hold;
  }, signal);
}
