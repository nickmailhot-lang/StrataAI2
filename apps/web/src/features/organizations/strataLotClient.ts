import { apiFetch } from '../../api/apiFetch';
import { boundedWorkRead, workRequest, WorkInputError, WorkRequestError } from '../../api/workManagement';
import { isNotificationProfile, notificationUuid as uuid } from '../notifications/notificationInbox';
import { isSourceDateTime } from '../auth/userDateTime';

export type LotData = { unitIdentifier: string; lotIdentifier: string; civicAddress: string | null;
  building: string | null; floor: string | null; type: string | null; status: 'ACTIVE' | 'INACTIVE' };
export type Lot = { id: string; organizationId: string; version: number; data: LotData; createdAt: string; updatedAt: string };
export type LotRevision = { lot: Lot; actorId: string; eventId: string; correlationId: string };
export const lotFields = [
  ['unitIdentifier', 'Unit identifier', 80], ['lotIdentifier', 'Lot identifier', 80],
  ['civicAddress', 'Civic address', 240], ['building', 'Building', 120], ['floor', 'Floor', 40], ['type', 'Unit type', 80],
] as const;
export const emptyLot: LotData = { unitIdentifier: '', lotIdentifier: '', civicAddress: null, building: null, floor: null, type: null, status: 'ACTIVE' };
const unavailable = () => new WorkRequestError(503, null);
const object = (value: unknown): Record<string, unknown> => {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw unavailable();
  return value as Record<string, unknown>;
};
function id(value: string) { if (!uuid(value)) throw new WorkInputError('The unit context is unavailable.'); return value.toLowerCase(); }
export function lotErrors(data: LotData): Partial<Record<keyof LotData, string>> {
  const errors: Partial<Record<keyof LotData, string>> = {};
  for (const [key, label, max] of lotFields) {
    const value = data[key]; const required = key === 'unitIdentifier' || key === 'lotIdentifier';
    if (value === null ? required : typeof value !== 'string' || !value.length || value.length > max
      || value.trim() !== value || /[\p{Cc}\p{Cf}]/u.test(value))
      errors[key] = `${label}: enter ${required ? 'a value' : 'a value or leave blank'} up to ${max} characters, without surrounding spaces or control characters.`;
  }
  if (!['ACTIVE', 'INACTIVE'].includes(data.status)) errors.status = 'Choose Active or Inactive.';
  return errors;
}
function parseData(value: unknown): LotData {
  const row = object(value);
  if (Object.keys(row).sort().join(',') !== Object.keys(emptyLot).sort().join(',') || Object.keys(lotErrors(row as LotData)).length) throw unavailable();
  return { ...row } as LotData;
}
export function parseLot(value: unknown, organization: string, lotId?: string): Lot {
  const row = object(value);
  if (!uuid(row.id) || row.organizationId !== organization || lotId && row.id !== lotId
    || !Number.isSafeInteger(row.version) || Number(row.version) < 1
    || !isSourceDateTime(row.createdAt) || !isSourceDateTime(row.updatedAt)) throw unavailable();
  return { id: row.id, organizationId: organization, version: Number(row.version), data: parseData(row.data), createdAt: row.createdAt, updatedAt: row.updatedAt };
}
async function account(signal: AbortSignal, expected?: string) {
  const profile = await workRequest<unknown>('/me', { signal });
  if (!isNotificationProfile(profile) || expected && profile.id !== expected) throw new WorkRequestError(401, null);
  return profile;
}
async function actor(signal: AbortSignal, expected?: string) { return (await account(signal, expected)).id; }
async function request(path: string, actorId: string, signal: AbortSignal, options: RequestInit = {}) {
  const headers = new Headers(options.headers); headers.set('X-StrataAI-Expected-Actor', actorId);
  const response = await apiFetch(path, { ...options, headers, signal }); signal.throwIfAborted();
  const body: unknown = await response.json().catch(() => undefined); signal.throwIfAborted();
  if (!response.ok) {
    const code = body && typeof body === 'object' && 'code' in body && typeof body.code === 'string'
      && /^(invalid_strata_lot_[a-z_]+|version_conflict|idempotency_key_(conflict|expired)|strata_lot_identifier_unavailable)$/.test(body.code) ? body.code : undefined;
    throw new WorkRequestError(response.status, response.headers.get('X-Correlation-ID'), code);
  }
  if (response.status !== 200) throw unavailable();
  return body;
}
const path = (organization: string) => `/organizations/${id(organization)}/strata-lots`;
async function admit(organization: string, actorId: string, signal: AbortSignal) {
  const row = object(await request(`/organizations/${id(organization)}`, actorId, signal));
  const org = object(row.organization);
  if (org.id !== organization || org.status !== 0 || row.role !== 0 && row.role !== 1) throw new WorkRequestError(404, null);
}
export function readLots(organization: string, signal: AbortSignal, expected?: string, after?: string) {
  return boundedWorkRead(async bounded => {
    const actorId = await actor(bounded, expected); await admit(organization, actorId, bounded);
    const row = object(await request(`${path(organization)}${after ? `?after=${id(after)}` : ''}`, actorId, bounded));
    if (row.organizationId !== organization || !Array.isArray(row.items) || row.items.length > 50) throw unavailable();
    const items = row.items.map(value => parseLot(value, organization)); let previous = after;
    for (const item of items) { if (previous && item.id <= previous) throw unavailable(); previous = item.id; }
    if (row.nextCursor !== null && (!uuid(row.nextCursor) || items.length !== 50 || row.nextCursor !== previous)) throw unavailable();
    await admit(organization, actorId, bounded); const profile = await account(bounded, actorId); bounded.throwIfAborted();
    return { actorId, items, nextCursor: row.nextCursor as string | null, preferences: { locale: profile.locale, timezone: profile.timezone } };
  }, signal);
}
export function readLot(organization: string, actorId: string, lotId: string, signal: AbortSignal) {
  return boundedWorkRead(async bounded => {
    await actor(bounded, actorId);
    const lot = parseLot(await request(`${path(organization)}/${id(lotId)}`, actorId, bounded), organization, lotId);
    await admit(organization, actorId, bounded); await actor(bounded, actorId); bounded.throwIfAborted(); return lot;
  }, signal);
}
// A refused creation may already exist, or may never have reached storage.
// Only current administrative admission can distinguish absence from withdrawal.
export function readLotIfPresent(organization: string, actorId: string, lotId: string, signal: AbortSignal) {
  return boundedWorkRead(async bounded => {
    await actor(bounded, actorId); await admit(organization, actorId, bounded);
    let value: unknown;
    try { value = await request(`${path(organization)}/${id(lotId)}`, actorId, bounded); }
    catch (reason) {
      if (!(reason instanceof WorkRequestError) || reason.status !== 404) throw reason;
      await admit(organization, actorId, bounded); await actor(bounded, actorId); bounded.throwIfAborted();
      return undefined;
    }
    const lot = parseLot(value, organization, lotId);
    await admit(organization, actorId, bounded); await actor(bounded, actorId); bounded.throwIfAborted(); return lot;
  }, signal);
}
export function readLotHistory(organization: string, actorId: string, lotId: string, signal: AbortSignal, before?: number) {
  if (before !== undefined && (!Number.isSafeInteger(before) || before < 1)) throw new WorkInputError('Reload unit history.');
  return boundedWorkRead(async bounded => {
    await actor(bounded, actorId);
    const row = object(await request(`${path(organization)}/${id(lotId)}/history${before ? `?beforeVersion=${before}` : ''}`, actorId, bounded));
    if (row.organizationId !== organization || row.strataLotId !== lotId || !Array.isArray(row.items) || row.items.length > 50) throw unavailable();
    let previous = before ?? Number.MAX_SAFE_INTEGER;
    const items = row.items.map(value => {
      const revision = object(value); const lot = parseLot(revision.lot, organization, lotId);
      if (lot.version >= previous || !uuid(revision.actorId) || !uuid(revision.eventId) || typeof revision.correlationId !== 'string') throw unavailable();
      previous = lot.version; return { lot, actorId: revision.actorId, eventId: revision.eventId, correlationId: revision.correlationId } as LotRevision;
    });
    if (row.nextBeforeVersion !== null && (items.length !== 50 || row.nextBeforeVersion !== previous)) throw unavailable();
    await admit(organization, actorId, bounded); await actor(bounded, actorId); bounded.throwIfAborted();
    return { items, nextBeforeVersion: row.nextBeforeVersion as number | null };
  }, signal);
}
// Immutable original bytes survive reload in this tab. Recovery never rebases a
// pending command onto a later revision or silently generates a new key.
export class LotIntent {
  constructor(readonly organization: string, readonly actorId: string, readonly lotId: string,
    readonly version: number, readonly data: LotData, readonly key: string = crypto.randomUUID()) {
    id(organization); id(actorId); id(lotId); id(key);
    if (!Number.isSafeInteger(version) || version < 0 || version >= Number.MAX_SAFE_INTEGER) throw new WorkInputError('Reload the current unit revision.');
    this.data = Object.freeze(parseData(data)); Object.freeze(this);
  }
  get body() { return JSON.stringify({ version: this.version, data: this.data }); }
  static restore(encoded: string) {
    const row = object(JSON.parse(encoded));
    const intent = new LotIntent(String(row.organization), String(row.actorId), String(row.lotId), Number(row.version), parseData(row.data), String(row.key));
    if (JSON.stringify(intent) !== encoded) throw new WorkInputError('The saved unit submission cannot be recovered.'); return intent;
  }
  submit(signal: AbortSignal) {
    return boundedWorkRead(async bounded => {
      await actor(bounded, this.actorId); await admit(this.organization, this.actorId, bounded);
      const lot = parseLot(await request(`${path(this.organization)}/${this.lotId}`, this.actorId, bounded,
        { method: 'PATCH', headers: { 'Content-Type': 'application/json', 'Idempotency-Key': this.key }, body: this.body }), this.organization, this.lotId);
      if (lot.version !== this.version + 1 || JSON.stringify(lot.data) !== JSON.stringify(this.data)) throw unavailable();
      await admit(this.organization, this.actorId, bounded); await actor(bounded, this.actorId); bounded.throwIfAborted(); return lot;
    }, signal);
  }
}
export const recoveryKey = (organization: string, actorId: string) => `strataai:unit-change:v1:${id(actorId)}:${id(organization)}`;
export function retainLotIntent(intent: LotIntent) {
  try {
    const key = recoveryKey(intent.organization, intent.actorId); const encoded = JSON.stringify(intent);
    const previous = sessionStorage.getItem(key);
    if (previous !== null && previous !== encoded) throw new WorkInputError('Recover the original unit change first.');
    sessionStorage.setItem(key, encoded);
    if (sessionStorage.getItem(key) !== encoded) throw new Error('Recovery retention unavailable');
  } catch (reason) {
    if (reason instanceof WorkInputError) throw reason;
    throw new WorkInputError('This browser could not retain the original submission. No new change was sent.');
  }
}
export function forgetLotChanges(organization?: string, exceptActor?: string) {
  try {
    for (let index = sessionStorage.length - 1; index >= 0; index--) {
      const key = sessionStorage.key(index);
      if (key?.startsWith('strataai:unit-change:v1:') && (!organization || key.endsWith(`:${organization}`))
        && (!exceptActor || !key.startsWith(`strataai:unit-change:v1:${exceptActor}:`))) sessionStorage.removeItem(key);
    }
  } catch { /* Withdrawal/sign-out cannot depend on browser storage. */ }
}
