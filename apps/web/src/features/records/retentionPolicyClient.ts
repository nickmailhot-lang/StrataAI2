import { apiFetch } from '../../api/apiFetch';
import { retentionProblemCode } from '../../api/retentionProblem';
import { boundedWorkRead, workRequest, WorkInputError, WorkRequestError } from '../../api/workManagement';
import { isNotificationProfile, notificationUuid } from '../notifications/notificationInbox';

export type RetentionPolicy = {
  id: string; organizationId: string; version: number;
  recordClass: 'operationalWork' | 'governance' | 'audit' | 'correspondence' | 'document' | 'ai';
  category: string; jurisdiction: string | null; anchor: 'created' | 'archived' | 'closed' | 'approved';
  retentionDays: number | null; action: 'preserve' | 'delete' | 'anonymize';
  source: string; notes: string | null; createdAt: string; updatedAt: string;
};
export type PolicyPage = { organizationId: string; items: RetentionPolicy[]; nextAfter: string | null };
export type PolicyRevision = { policy: RetentionPolicy; actorId: string; actorLabel: string; membershipId: string;
  membershipVersion: number; actingCapacity: 0 | 1; eventId: string };
export type PolicyHistory = { organizationId: string; policyId: string; items: PolicyRevision[]; nextBeforeVersion: number | null };
export type RetentionPolicyInput = Pick<RetentionPolicy, 'recordClass' | 'category' | 'jurisdiction' | 'anchor' | 'retentionDays' | 'action' | 'source' | 'notes'>;

const inputKeys = ['recordClass', 'category', 'jurisdiction', 'anchor', 'retentionDays', 'action', 'source', 'notes'];
export function parseRetentionPolicyInput(value: unknown): RetentionPolicyInput {
  try {
    const row = object(value);
    if (!exact(row, [...inputKeys])) throw new Error();
    // Use the same field contract for reviewed input and authoritative sources.
    const context = '11111111-1111-4111-8111-111111111111';
    const policy = parseRetentionPolicy({ ...row, id: context, organizationId: context, version: 1,
      createdAt: '2000-01-01T00:00:00Z', updatedAt: '2000-01-01T00:00:00Z' }, context);
    return { recordClass: policy.recordClass, category: policy.category, jurisdiction: policy.jurisdiction,
      anchor: policy.anchor, retentionDays: policy.retentionDays, action: policy.action, source: policy.source, notes: policy.notes };
  } catch { throw new WorkInputError('Provide a supported record class, category, applicability, retention rule and reviewed source.'); }
}

function unavailable(): never { throw new WorkRequestError(503, null); }
function object(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return unavailable();
  return value as Record<string, unknown>;
}
function exact(row: Record<string, unknown>, keys: string[]) { return Object.keys(row).sort().join(',') === keys.sort().join(','); }
function positive(value: unknown): value is number { return Number.isSafeInteger(value) && (value as number) > 0; }
function text(value: unknown, max: number, multiline = false): value is string {
  return typeof value === 'string' && value.length > 0 && value.length <= max && value.trim() === value
    && !Array.from(value).some(character => {
      const code = character.codePointAt(0)!;
      return (code < 32 || code >= 127 && code <= 159) && !(multiline && ['\r', '\n', '\t'].includes(character));
    });
}
function instant(value: unknown): value is string {
  return typeof value === 'string' && /^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(value) && Number.isFinite(Date.parse(value));
}
function preciseTime(value: unknown): bigint | null {
  if (!instant(value) || value.startsWith('0000-')) return null;
  const match = /^(\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d)(?:\.(\d{1,7}))?(?:Z|\+00:00)$/.exec(value)!;
  const milliseconds = Date.parse(match[1] + 'Z');
  if (new Date(milliseconds).toISOString().slice(0, 19) !== match[1]) return null;
  return BigInt(milliseconds) * 10_000n + BigInt((match[2] ?? '').padEnd(7, '0'));
}
export function parseRetentionPolicy(value: unknown, organization: string): RetentionPolicy {
  const row = object(value);
  const created = preciseTime(row.createdAt); const updated = preciseTime(row.updatedAt);
  if (!exact(row, ['id', 'organizationId', 'version', 'recordClass', 'category', 'jurisdiction', 'anchor', 'retentionDays', 'action', 'source', 'notes', 'createdAt', 'updatedAt'])
    || !notificationUuid(row.id) || row.id !== row.id.toLowerCase() || row.organizationId !== organization || !positive(row.version)
    || !['operationalWork', 'governance', 'audit', 'correspondence', 'document', 'ai'].includes(row.recordClass as string)
    || typeof row.category !== 'string' || !/^[A-Za-z0-9_.-]{1,64}$/.test(row.category)
    || row.jurisdiction !== null && !text(row.jurisdiction, 120)
    || !['created', 'archived', 'closed', 'approved'].includes(row.anchor as string)
    || !['preserve', 'delete', 'anonymize'].includes(row.action as string)
    || row.retentionDays === null && row.action !== 'preserve'
    || row.retentionDays !== null && (!Number.isInteger(row.retentionDays) || (row.retentionDays as number) < 0 || (row.retentionDays as number) > 3_652_500)
    || !text(row.source, 1000, true) || row.notes !== null && !text(row.notes, 2000, true)
    || created === null || updated === null || updated < created) return unavailable();
  return row as RetentionPolicy;
}
export function parsePolicyPage(value: unknown, organization: string, after?: string): PolicyPage {
  const page = object(value);
  if (!exact(page, ['organizationId', 'items', 'nextAfter']) || page.organizationId !== organization
    || !Array.isArray(page.items) || page.items.length > 50) return unavailable();
  let previous = after;
  const items = page.items.map(value => {
    const row = parseRetentionPolicy(value, organization);
    if (previous && row.id <= previous) return unavailable(); previous = row.id; return row;
  });
  if (page.nextAfter !== null && (!notificationUuid(page.nextAfter) || items.length !== 50 || page.nextAfter !== previous)) return unavailable();
  return { organizationId: organization, items, nextAfter: page.nextAfter as string | null };
}
export function parsePolicyHistory(value: unknown, organization: string, policy: string, before?: number): PolicyHistory {
  const page = object(value);
  if (!exact(page, ['organizationId', 'policyId', 'items', 'nextBeforeVersion']) || page.organizationId !== organization || page.policyId !== policy
    || !Array.isArray(page.items) || page.items.length > 50) return unavailable();
  let previous = before; const events = new Set<string>();
  let scope: RetentionPolicy | undefined; let previousAt: bigint | undefined;
  const items = page.items.map((value, index) => {
    const row = object(value); const source = parseRetentionPolicy(row.policy, organization);
    const updated = preciseTime(source.updatedAt)!;
    if (!exact(row, ['policy', 'actorId', 'actorLabel', 'membershipId', 'membershipVersion', 'actingCapacity', 'eventId'])
      || source.id !== policy || previous !== undefined && (source.version >= previous || index > 0 && source.version !== previous - 1)
      || previousAt !== undefined && updated > previousAt
      || scope !== undefined && (source.recordClass !== scope.recordClass || source.category.toUpperCase() !== scope.category.toUpperCase()
        || source.jurisdiction !== scope.jurisdiction || source.createdAt !== scope.createdAt)
      || !notificationUuid(row.actorId) || !notificationUuid(row.membershipId) || !positive(row.membershipVersion)
      || !text(row.actorLabel, 160) || row.actingCapacity !== 0 && row.actingCapacity !== 1
      || !notificationUuid(row.eventId) || events.has(row.eventId)) return unavailable();
    scope ??= source; previousAt = updated; previous = source.version; events.add(row.eventId); return { ...row, policy: source } as PolicyRevision;
  });
  if (page.nextBeforeVersion !== null && (!positive(page.nextBeforeVersion) || items.length !== 50 || page.nextBeforeVersion !== previous)) return unavailable();
  return { organizationId: organization, policyId: policy, items, nextBeforeVersion: page.nextBeforeVersion as number | null };
}
function id(value: string) {
  if (!notificationUuid(value)) throw new WorkInputError('The policy context is unavailable.'); return value.toLowerCase();
}
async function actor(signal: AbortSignal, expected?: string) {
  const profile = await workRequest<unknown>('/me', { signal });
  if (!isNotificationProfile(profile) || expected && profile.id.toLowerCase() !== expected) throw new WorkRequestError(401, null);
  return profile.id.toLowerCase();
}
async function request(path: string, expected: string, signal: AbortSignal) {
  const response = await apiFetch(path, { signal, headers: { 'X-StrataAI-Expected-Actor': expected } });
  signal.throwIfAborted();
  if (!response.ok) throw new WorkRequestError(response.status, null);
  if (response.status !== 200) return unavailable();
  const body: unknown = await response.json().catch(() => undefined); signal.throwIfAborted(); return body;
}
// The maximum UUID yields an empty directory page while freshly enforcing the
// same private Owner/Admin authority, without retaining another policy body.
const maximumId = 'ffffffff-ffff-ffff-ffff-ffffffffffff';
async function finalAdmission(organization: string, expected: string, signal: AbortSignal) {
  const page = parsePolicyPage(await request(`/organizations/${organization}/retention-policies?after=${maximumId}`, expected, signal), organization, maximumId);
  if (page.items.length) return unavailable(); await actor(signal, expected); signal.throwIfAborted();
}
export function readRetentionPolicies(organizationId: string, signal: AbortSignal, expectedActor?: string, after?: string) {
  const organization = id(organizationId); const expected = expectedActor === undefined ? undefined : id(expectedActor);
  const boundary = after === undefined ? undefined : id(after);
  return boundedWorkRead(async bounded => {
    const actorId = await actor(bounded, expected);
    const page = parsePolicyPage(await request(`/organizations/${organization}/retention-policies${boundary ? `?after=${boundary}` : ''}`, actorId, bounded), organization, boundary);
    await finalAdmission(organization, actorId, bounded); return { actorId, page };
  }, signal);
}
export function readRetentionHistory(organizationId: string, actorId: string, policyId: string, signal: AbortSignal, before?: number) {
  const organization = id(organizationId); const expected = id(actorId); const policy = id(policyId);
  if (before !== undefined && !positive(before)) throw new WorkInputError('The history boundary is unavailable.');
  return boundedWorkRead(async bounded => {
    await actor(bounded, expected);
    const page = parsePolicyHistory(await request(`/organizations/${organization}/retention-policies/${policy}/history${before === undefined ? '' : `?beforeVersion=${before}`}`, expected, bounded), organization, policy, before);
    await finalAdmission(organization, expected, bounded); return page;
  }, signal);
}

// A reviewed change owns its original request bytes. Later reads, navigation,
// timeouts and retries must not replace its identity, revision or retry key.
export class RetentionPolicyChangeIntent {
  readonly organizationId: string;
  readonly actorId: string;
  readonly policyId: string;
  readonly expectedVersion: number;
  readonly key: string;
  readonly #body: string;
  private constructor(organization: string, actorId: string, policy: string, version: number, input: unknown, key: string) {
    this.organizationId = id(organization); this.actorId = id(actorId); this.policyId = id(policy); this.key = id(key);
    if (!Number.isSafeInteger(version) || version < 0 || version >= Number.MAX_SAFE_INTEGER)
      throw new WorkInputError('The reviewed policy revision is unavailable.');
    this.expectedVersion = version;
    this.#body = JSON.stringify({ version, policy: parseRetentionPolicyInput(input) });
    if (new TextEncoder().encode(this.#body).length > 32_768) throw new WorkInputError('The reviewed policy exceeds the submission limit.');
    Object.freeze(this);
  }
  static review(organization: string, actorId: string, policy: string, version: number, input: RetentionPolicyInput, key = crypto.randomUUID()) {
    return new RetentionPolicyChangeIntent(organization, actorId, policy, version, input, key);
  }
  reviewedPolicy(): RetentionPolicyInput { return parseRetentionPolicyInput((JSON.parse(this.#body) as { policy: unknown }).policy); }
  recoveryRecord() { return { organizationId: this.organizationId, actorId: this.actorId, policyId: this.policyId, key: this.key, body: this.#body }; }
  static restore(value: unknown) {
    try {
      const row = object(value);
      if (!exact(row, ['organizationId', 'actorId', 'policyId', 'key', 'body']) || typeof row.body !== 'string'
        || new TextEncoder().encode(row.body).length > 32_768) throw new Error();
      const body = object(JSON.parse(row.body));
      if (!exact(body, ['version', 'policy']) || typeof body.version !== 'number'
        || !notificationUuid(row.organizationId) || !notificationUuid(row.actorId) || !notificationUuid(row.policyId) || !notificationUuid(row.key)) throw new Error();
      const restored = new RetentionPolicyChangeIntent(row.organizationId, row.actorId, row.policyId, body.version, body.policy, row.key);
      if (restored.#body !== row.body) throw new Error();
      return restored;
    } catch { throw new WorkInputError('The original policy submission could not be restored.'); }
  }
  submit(signal: AbortSignal, sent: () => void = () => {}) {
    return boundedWorkRead(async bounded => {
      await actor(bounded, this.actorId);
      await finalAdmission(this.organizationId, this.actorId, bounded); bounded.throwIfAborted();
      sent();
      const response = await apiFetch(`/organizations/${this.organizationId}/retention-policies/${this.policyId}`, {
        method: 'PATCH', signal: bounded, headers: { 'Content-Type': 'application/json', 'Idempotency-Key': this.key,
          'X-StrataAI-Expected-Actor': this.actorId }, body: this.#body,
      });
      bounded.throwIfAborted();
      if (!response.ok) {
        const problem: unknown = await response.json().catch(() => undefined); bounded.throwIfAborted();
        const code = problem && typeof problem === 'object' && 'code' in problem ? retentionProblemCode(problem.code, response.status) : undefined;
        throw new WorkRequestError(response.status, response.headers.get('X-Correlation-ID'), code);
      }
      if (response.status !== 200) return unavailable();
      const value: unknown = await response.json().catch(() => undefined); bounded.throwIfAborted();
      const result = parseRetentionPolicy(value, this.organizationId);
      if (result.id !== this.policyId || result.version !== this.expectedVersion + 1
        || JSON.stringify(parseRetentionPolicyInput(Object.fromEntries(inputKeys.map(key => [key, result[key as keyof RetentionPolicy]]))))
          !== JSON.stringify(this.reviewedPolicy())) return unavailable();
      await finalAdmission(this.organizationId, this.actorId, bounded);
      // An original receipt may predate today's policy; refresh separately.
      return result;
    }, signal);
  }
}
