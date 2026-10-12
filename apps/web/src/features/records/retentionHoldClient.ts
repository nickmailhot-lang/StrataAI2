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
export type HoldRevision = { hold:RetentionHold;actorId:string;actorLabel:string;actingCapacity:0|1;
  eventId:string;correlationId:string;createdAt:string };
export type HoldTimeline = {current:RetentionHold;revisions:HoldRevision[]};
export type HoldDirectory = {organizationId:string;items:RetentionHold[];nextAfter:string|null};
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
export function readHoldDirectory(organization:string,actor:string,signal:AbortSignal,after?:string) {
  const tenant=identity(organization),boundary=after===undefined?undefined:identity(after);
  return boundedWorkRead(async bounded=>{
    await readRetentionPolicies(tenant,bounded,actor,admissionCursor);
    const response=await apiFetch(`/organizations/${tenant}/retention-holds${boundary?`?after=${boundary}`:''}`,
      {signal:bounded,headers:{'X-StrataAI-Expected-Actor':actor}});
    if (!response.ok) throw new WorkRequestError(response.status,null);
    const value:unknown=await response.json();bounded.throwIfAborted();
    if (!value || typeof value!=='object' || Array.isArray(value) || Object.keys(value).sort().join(',')!=='items,nextAfter,organizationId') return unavailable();
    const row=value as HoldDirectory;
    if (row.organizationId!==tenant || !Array.isArray(row.items) || row.items.length>25) return unavailable();
    let previous=boundary;
    const items=row.items.map(item=>{
      if (!item || typeof item!=='object' || !notificationUuid(item.id) || item.id!==item.id.toLowerCase()) return unavailable();
      const hold=parseHold(item,tenant,item.id);
      if (hold.version>2 || previous!==undefined && hold.id<=previous) return unavailable();
      previous=hold.id;return hold;
    });
    if (row.nextAfter!==null && (items.length!==25 || row.nextAfter!==items.at(-1)!.id)) return unavailable();
    await readRetentionPolicies(tenant,bounded,actor,admissionCursor);return {organizationId:tenant,items,nextAfter:row.nextAfter};
  },signal);
}
export function readHoldTimeline(organization:string,id:string,actor:string,signal:AbortSignal) {
  return boundedWorkRead(async bounded=>{
    await readRetentionPolicies(organization,bounded,actor,admissionCursor);
    const response=await apiFetch(`${path(organization,id)}/history`,{signal:bounded,headers:{'X-StrataAI-Expected-Actor':actor}});
    if (!response.ok) throw new WorkRequestError(response.status,null);
    const body:unknown=await response.json();bounded.throwIfAborted();
    if (!body || typeof body!=='object' || Array.isArray(body) || Object.keys(body).sort().join(',')!=='current,revisions') return unavailable();
    const row=body as HoldTimeline,current=parseHold(row.current,organization,id);
    if (current.version>2 || !Array.isArray(row.revisions) || row.revisions.length!==current.version) return unavailable();
    const events=new Set<string>();
    const revisions=row.revisions.map((revision,index)=>{
      if (!revision || typeof revision!=='object' || Array.isArray(revision)
        || Object.keys(revision).sort().join(',')!=='actingCapacity,actorId,actorLabel,correlationId,createdAt,eventId,hold') return unavailable();
      const hold=parseHold(revision.hold,organization,id);
      if (hold.version!==index+1 || hold.status!==(index===0?'active':'released')
        || (['recordClass','category','recordId','reason','appliedBy','appliedAt'] as const).some(key=>hold[key]!==current[key])
        || revision.actorId!==(hold.releasedBy??hold.appliedBy) || ![0,1].includes(revision.actingCapacity)
        || typeof revision.actorLabel!=='string' || !revision.actorLabel.trim() || revision.actorLabel.length>160
        || Array.from(revision.actorLabel).some(c=>c.charCodeAt(0)<32 || c.charCodeAt(0)>=127 && c.charCodeAt(0)<=159)
        || !notificationUuid(revision.eventId) || events.has(revision.eventId)
        || typeof revision.correlationId!=='string' || !revision.correlationId.trim() || revision.correlationId.length>256
        || revision.createdAt!==(hold.releasedAt??hold.appliedAt)) return unavailable();
      events.add(revision.eventId);return {...revision,hold};
    });
    if ((Object.keys(current) as (keyof RetentionHold)[]).some(key=>revisions.at(-1)!.hold[key]!==current[key])) return unavailable();
    await readRetentionPolicies(organization,bounded,actor,admissionCursor);return {current,revisions};
  },signal);
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
export function forgetHoldIntent(actor: string, organization: string) {
  const key = storageKey(actor,organization); sessionStorage.removeItem(key);
  if (sessionStorage.getItem(key) !== null) throw new WorkInputError('Original hold recovery could not be retired.');
}
export function forgetHoldIntents(actor?: string, organization?: string) {
  const prefix = 'strataai:hold-command:v1:';
  try {
    for (let index = sessionStorage.length - 1; index >= 0; index--) {
      const key = sessionStorage.key(index); if (!key?.startsWith(prefix)) continue;
      const parts = key.slice(prefix.length).split(':');
      if (parts.length === 2 && parts.every(notificationUuid)
        && (!actor || parts[0] === identity(actor)) && (!organization || parts[1] === identity(organization))) sessionStorage.removeItem(key);
    }
  } catch { /* Definitive withdrawal cannot depend on browser storage. */ }
}
export function forgetForeignHoldIntents(actor: string) {
  const prefix = 'strataai:hold-command:v1:'; const current = identity(actor);
  for (let index = sessionStorage.length - 1; index >= 0; index--) {
    const key = sessionStorage.key(index); if (!key?.startsWith(prefix)) continue;
    const parts = key.slice(prefix.length).split(':');
    if (parts.length === 2 && parts.every(notificationUuid) && parts[0] !== current) {
      sessionStorage.removeItem(key);
      if (sessionStorage.getItem(key) !== null) throw new WorkInputError('Foreign hold recovery could not be retired.');
    }
  }
}
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
