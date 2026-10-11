import { apiFetch } from '../../api/apiFetch';
import { boundedWorkRead, workRequest, WorkInputError, WorkRequestError } from '../../api/workManagement';
import { isNotificationProfile, notificationInstant, notificationUuid } from '../notifications/notificationInbox';

export type RetentionSourceKind = 'card' | 'cardComment' | 'attachment';
export type RetentionSourceReview = {
  organizationId: string; recordId: string; kind: RetentionSourceKind; parentCardId: string | null;
  cardId: string; boardId: string; listId: string; cardVersion: number; listVersion: number; sourceVersion: number;
  cardState: 'active' | 'archived'; listState: 'active' | 'archived'; sourceState: 'active' | 'archived'; createdAt: string;
};
export type RetentionSourceReceipt = {
  reference: { organizationId: string; recordClass: 'operationalWork' | 'document'; recordId: string; kind: RetentionSourceKind;
    parentCardId: string | null; contextVersion: 1; originalBoardId: string; sourceVersion: number };
  category: string; jurisdiction: string | null; actorId: string; eventId: string; registeredAt: string;
};
const reviewKeys = ['organizationId', 'recordId', 'kind', 'parentCardId', 'cardId', 'boardId', 'listId', 'cardVersion',
  'listVersion', 'sourceVersion', 'cardState', 'listState', 'sourceState', 'createdAt'];
function unavailable(): never { throw new WorkRequestError(503, null); }
function object(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return unavailable();
  return value as Record<string, unknown>;
}
function exact(row: Record<string, unknown>, keys: string[]) {
  return Object.keys(row).sort().join(',') === [...keys].sort().join(',');
}
function uuid(value: unknown): value is string { return notificationUuid(value) && value === value.toLowerCase(); }
function positive(value: unknown): value is number { return Number.isSafeInteger(value) && (value as number) > 0; }
function kind(value: unknown): value is RetentionSourceKind { return ['card', 'cardComment', 'attachment'].includes(value as string); }
function identity(value: string) {
  if (!notificationUuid(value)) throw new WorkInputError('The source context is unavailable.'); return value.toLowerCase();
}
function applicability(category: unknown, jurisdiction: unknown) {
  if (typeof category !== 'string' || !/^[A-Za-z0-9_.-]{1,64}$/.test(category)
    || jurisdiction !== null && (typeof jurisdiction !== 'string' || !jurisdiction.length || jurisdiction.length > 120
      || jurisdiction.trim() !== jurisdiction || Array.from(jurisdiction).some(character => {
        const code = character.codePointAt(0)!; return code < 32 || code >= 127 && code <= 159;
      })))
    throw new WorkInputError('Provide an explicit category and supported jurisdiction.');
  return { category, jurisdiction: jurisdiction as string | null };
}
export function parseRetentionSourceReview(value: unknown, organization: string, producer: RetentionSourceKind, record: string): RetentionSourceReview {
  const row = object(value);
  try {
    if (!exact(row, reviewKeys) || !uuid(row.organizationId) || row.organizationId !== organization || row.kind !== producer
      || !kind(row.kind) || !uuid(row.recordId) || row.recordId !== record || !uuid(row.cardId) || !uuid(row.boardId) || !uuid(row.listId)
      || row.kind === 'card' && (row.parentCardId !== null || row.cardId !== row.recordId || row.sourceVersion !== row.cardVersion || row.sourceState !== row.cardState)
      || row.kind !== 'card' && (row.parentCardId !== row.cardId || row.recordId === row.cardId)
      || !positive(row.cardVersion) || !positive(row.listVersion) || !positive(row.sourceVersion)
      || ![row.cardState, row.listState, row.sourceState].every(state => state === 'active' || state === 'archived')
      || typeof row.createdAt !== 'string' || row.createdAt.startsWith('0000-')) return unavailable();
    notificationInstant(row.createdAt);
    return Object.fromEntries(reviewKeys.map(key => [key, row[key]])) as RetentionSourceReview;
  } catch { return unavailable(); }
}
async function actor(signal: AbortSignal, expected?: string) {
  const profile = await workRequest<unknown>('/me', { signal });
  if (!isNotificationProfile(profile) || expected && profile.id.toLowerCase() !== expected) throw new WorkRequestError(401, null);
  return profile.id.toLowerCase();
}
function path(review: Pick<RetentionSourceReview, 'organizationId' | 'kind' | 'recordId'>) {
  return `/organizations/${review.organizationId}/retention-sources/${review.kind}/${review.recordId}`;
}
export function readRetentionSourceReview(organizationId: string, producer: RetentionSourceKind, recordId: string,
  signal: AbortSignal, expectedActor?: string) {
  const organization = identity(organizationId), record = identity(recordId);
  if (!kind(producer)) throw new WorkInputError('The source type is unavailable.');
  const expected = expectedActor === undefined ? undefined : identity(expectedActor);
  return boundedWorkRead(async bounded => {
    const actorId = await actor(bounded, expected);
    const response = await apiFetch(`${path({ organizationId: organization, kind: producer, recordId: record })}/review`,
      { signal: bounded, headers: { 'X-StrataAI-Expected-Actor': actorId } });
    bounded.throwIfAborted();
    if (!response.ok) throw new WorkRequestError(response.status, null);
    if (response.status !== 200) return unavailable();
    const body: unknown = await response.json().catch(() => undefined); bounded.throwIfAborted();
    const review = parseRetentionSourceReview(body, organization, producer, record);
    await actor(bounded, actorId); bounded.throwIfAborted(); return { actorId, review };
  }, signal);
}

// The original body is private and immutable. Fresh source reads must not replace
// its relationship/version tuple when recovering an uncertain acknowledgment.
export class RetentionSourceClassificationIntent {
  readonly actorId: string;
  readonly organizationId: string;
  readonly key: string;
  readonly #body: string;
  private constructor(actorId: string, review: RetentionSourceReview, category: string, jurisdiction: string | null, key: string) {
    this.actorId = identity(actorId); this.organizationId = identity(review.organizationId); this.key = identity(key);
    const evidence = parseRetentionSourceReview(review, this.organizationId, review.kind, review.recordId);
    this.#body = JSON.stringify({ review: evidence, ...applicability(category, jurisdiction), confirmed: true });
    Object.freeze(this);
  }
  static review(actorId: string, review: RetentionSourceReview, category: string, jurisdiction: string | null,
    confirmed: boolean, key = crypto.randomUUID()) {
    if (confirmed !== true) throw new WorkInputError('Explicitly confirm the reviewed source classification.');
    return new RetentionSourceClassificationIntent(actorId, review, category, jurisdiction, key);
  }
  reviewed(): { review: RetentionSourceReview; category: string; jurisdiction: string | null; confirmed: true } { return JSON.parse(this.#body); }
  recoveryRecord() { return { actorId: this.actorId, organizationId: this.organizationId, key: this.key, body: this.#body }; }
  static restore(value: unknown) {
    try {
      const row = object(value);
      if (!exact(row, ['actorId', 'organizationId', 'key', 'body']) || !uuid(row.actorId) || !uuid(row.organizationId) || !uuid(row.key)
        || typeof row.body !== 'string' || new TextEncoder().encode(row.body).length > 32_768) throw new Error();
      const body = object(JSON.parse(row.body)), evidence = object(body.review);
      if (!exact(body, ['review', 'category', 'jurisdiction', 'confirmed']) || body.confirmed !== true
        || !kind(evidence.kind) || !uuid(evidence.recordId) || typeof body.category !== 'string'
        || body.jurisdiction !== null && typeof body.jurisdiction !== 'string') throw new Error();
      const review = parseRetentionSourceReview(evidence, row.organizationId, evidence.kind, evidence.recordId);
      const intent = new RetentionSourceClassificationIntent(row.actorId, review, body.category, body.jurisdiction, row.key);
      if (intent.#body !== row.body) throw new Error(); return intent;
    } catch { throw new WorkInputError('The original source classification could not be restored.'); }
  }
  parseReceipt(value: unknown): RetentionSourceReceipt {
    try {
      const row = object(value), reference = object(row.reference), { review, category, jurisdiction } = this.reviewed();
      if (!exact(row, ['reference', 'category', 'jurisdiction', 'actorId', 'eventId', 'registeredAt'])
        || !exact(reference, ['organizationId', 'recordClass', 'recordId', 'kind', 'parentCardId', 'contextVersion', 'originalBoardId', 'sourceVersion'])
        || reference.organizationId !== review.organizationId || reference.recordId !== review.recordId || reference.kind !== review.kind
        || reference.recordClass !== (review.kind === 'attachment' ? 'document' : 'operationalWork') || reference.parentCardId !== review.parentCardId
        || reference.contextVersion !== 1 || reference.originalBoardId !== review.boardId || reference.sourceVersion !== review.sourceVersion
        || row.category !== category || row.jurisdiction !== jurisdiction || row.actorId !== this.actorId || !uuid(row.eventId)
        || typeof row.registeredAt !== 'string' || row.registeredAt.startsWith('0000-')
        || notificationInstant(row.registeredAt).ticks < notificationInstant(review.createdAt).ticks) return unavailable();
      return row as RetentionSourceReceipt;
    } catch { return unavailable(); }
  }
  submit(signal: AbortSignal, sent: () => void = () => {}) {
    return boundedWorkRead(async bounded => {
      await actor(bounded, this.actorId); bounded.throwIfAborted(); sent();
      const response = await apiFetch(`${path(this.reviewed().review)}/classification`, { method: 'POST', signal: bounded,
        headers: { 'Content-Type': 'application/json', 'Idempotency-Key': this.key, 'X-StrataAI-Expected-Actor': this.actorId }, body: this.#body });
      bounded.throwIfAborted();
      if (!response.ok) {
        const problem: unknown = await response.json().catch(() => undefined); bounded.throwIfAborted();
        const code = problem && typeof problem === 'object' && 'code' in problem && typeof problem.code === 'string'
          && response.status === 409 && ['idempotency_key_conflict', 'idempotency_key_expired'].includes(problem.code) ? problem.code : undefined;
        throw new WorkRequestError(response.status, response.headers.get('X-Correlation-ID'), code);
      }
      if (response.status !== 200) return unavailable();
      const body: unknown = await response.json().catch(() => undefined); bounded.throwIfAborted();
      const receipt = this.parseReceipt(body); await actor(bounded, this.actorId); bounded.throwIfAborted(); return receipt;
    }, signal);
  }
}
