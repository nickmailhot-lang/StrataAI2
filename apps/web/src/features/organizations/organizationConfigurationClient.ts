import { apiFetch } from '../../api/apiFetch';
import { configurationProblemCode } from '../../api/configurationProblem';
import { boundedWorkRead, workRequest, WorkInputError, WorkRequestError } from '../../api/workManagement';
import { isNotificationProfile, notificationUuid } from '../notifications/notificationInbox';
import type { IntakeOption } from './OrganizationConfigurationForm';
import { parseConfigurationHistory, parseConfigurationRevision, parseConfigurationView, parseOrganizationConfiguration,
  type OrganizationConfiguration } from './organizationConfiguration';

function identifier(value: unknown): string {
  if (!notificationUuid(value)) throw new WorkInputError('The configuration context is unavailable.');
  return value.toLowerCase();
}
function version(value: number, changing = false) {
  if (!Number.isSafeInteger(value) || value < 0 || changing && value >= Number.MAX_SAFE_INTEGER)
    throw new WorkInputError('The reviewed configuration revision is unavailable.');
  return value;
}
async function actor(signal: AbortSignal, expected?: string): Promise<string> {
  const profile = await workRequest<unknown>('/me', { signal });
  if (!isNotificationProfile(profile) || expected && profile.id.toLowerCase() !== expected)
    throw new WorkRequestError(401, null);
  return profile.id.toLowerCase();
}
async function request(path: string, actorId: string, signal: AbortSignal, options: RequestInit = {}): Promise<unknown> {
  signal.throwIfAborted(); const headers = new Headers(options.headers);
  headers.set('X-StrataAI-Expected-Actor', actorId);
  const response = await apiFetch(path, { ...options, headers, signal });
  signal.throwIfAborted();
  const body: unknown = await response.json().catch(() => undefined);
  signal.throwIfAborted();
  if (!response.ok) {
    const candidate = body && typeof body === 'object' && 'code' in body ? body.code : undefined;
    const code = configurationProblemCode(candidate, response.status)
      ?? (response.status === 409 && (candidate === 'version_conflict' || candidate === 'idempotency_key_expired') ? candidate : undefined);
    throw new WorkRequestError(response.status, response.headers.get('X-Correlation-ID'), code);
  }
  if (response.status !== 200) throw new WorkRequestError(503, null);
  return body;
}
function path(organizationId: string) { return `/organizations/${organizationId}/configuration`; }

export function readOrganizationConfiguration(organizationId: string, signal: AbortSignal, expectedActor?: string) {
  const id = identifier(organizationId); const expected = expectedActor === undefined ? undefined : identifier(expectedActor);
  return boundedWorkRead(async bounded => {
    const actorId = await actor(bounded, expected);
    const view = parseConfigurationView(await request(path(id), actorId, bounded), id);
    await actor(bounded, actorId); bounded.throwIfAborted();
    return { actorId, view };
  }, signal);
}
export function readOrganizationConfigurationHistory(organizationId: string, actorId: string, signal: AbortSignal, beforeVersion?: number) {
  const id = identifier(organizationId); const expected = identifier(actorId);
  if (beforeVersion !== undefined && version(beforeVersion) === 0) throw new WorkInputError('The history boundary is unavailable.');
  return boundedWorkRead(async bounded => {
    await actor(bounded, expected);
    const result = parseConfigurationHistory(await request(`${path(id)}/history${beforeVersion === undefined ? '' : `?beforeVersion=${beforeVersion}`}`,
      expected, bounded), id, beforeVersion);
    await actor(bounded, expected); bounded.throwIfAborted(); return result;
  }, signal);
}

async function admittedIntake<T>(organizationId: string, actorId: string, signal: AbortSignal,
  read: (organization: string, actor: string, bounded: AbortSignal) => Promise<T>) {
  const id = identifier(organizationId); const expected = identifier(actorId);
  return boundedWorkRead(async bounded => {
    await actor(bounded, expected);
    parseConfigurationView(await request(path(id), expected, bounded), id);
    const result = await read(id, expected, bounded);
    // Board membership alone must not keep a withdrawn administrator's private
    // configuration picker open. Recheck configuration authority at the end.
    parseConfigurationView(await request(path(id), expected, bounded), id);
    await actor(bounded, expected); bounded.throwIfAborted(); return result;
  }, signal);
}
function intakeOption(value: unknown): IntakeOption {
  const row = value as Record<string, unknown> | null;
  if (!row || !notificationUuid(row.id) || typeof row.name !== 'string' || !row.name.trim() || row.name.length > 160)
    throw new WorkRequestError(503, null);
  return { id: row.id.toLowerCase(), name: row.name };
}
export function readConfigurationIntakeBoards(organizationId: string, actorId: string, signal: AbortSignal, after?: string) {
  const cursor = after === undefined ? undefined : identifier(after);
  return admittedIntake(organizationId, actorId, signal, async (id, expected, bounded) => {
    const page = await request(`/organizations/${id}/boards/directory${cursor ? `?after=${cursor}` : ''}`, expected, bounded) as Record<string, unknown> | null;
    if (!page || page.organizationId !== id || !Array.isArray(page.items) || page.items.length > 50) throw new WorkRequestError(503, null);
    let previous = cursor;
    const items = page.items.map(value => {
      const option = intakeOption(value); const row = value as Record<string, unknown>;
      if (previous && option.id <= previous || !Number.isSafeInteger(row.version) || (row.version as number) < 1) throw new WorkRequestError(503, null);
      previous = option.id; return option;
    });
    if (page.nextCursor !== null && !notificationUuid(page.nextCursor)) throw new WorkRequestError(503, null);
    const next = page.nextCursor === null ? null : (page.nextCursor as string).toLowerCase();
    if (next !== null && (items.length !== 50 || next !== items.at(-1)!.id)) throw new WorkRequestError(503, null);
    return { items, nextCursor: next };
  });
}
export function readConfigurationIntakeBoard(organizationId: string, actorId: string, boardId: string, signal: AbortSignal) {
  const selected = identifier(boardId);
  return admittedIntake(organizationId, actorId, signal, async (id, expected, bounded) => {
    const snapshot = await request(`/boards/${selected}`, expected, bounded) as Record<string, unknown> | null;
    const board = snapshot?.board as Record<string, unknown> | null;
    const access = snapshot?.access as Record<string, unknown> | null;
    if (!snapshot || !board || board.organizationId !== id || board.id !== selected || board.lifecycleState !== 'active'
      || !access || access.canAdminister !== true || !Array.isArray(snapshot.lists)) throw new WorkRequestError(404, null);
    const option = intakeOption(board); const seen = new Set<string>();
    const lists = snapshot.lists.map(value => {
      const item = value as Record<string, unknown> | null; const list = item?.list as Record<string, unknown> | null;
      if (!list || list.organizationId !== id || list.boardId !== selected || list.lifecycleState !== 'active') throw new WorkRequestError(503, null);
      const result = intakeOption(list); if (seen.has(result.id)) throw new WorkRequestError(503, null);
      seen.add(result.id); return result;
    });
    // Project only reviewed relationship options; Card data is never retained.
    return { board: option, lists };
  });
}

// A reviewed intent retains exact bytes/key/account across uncertain delivery.
// The caller keeps this object until the original acknowledgment is recovered or
// a definitive refusal is reviewed. A newer GET cannot acknowledge this intent.
export class ConfigurationChangeIntent {
  readonly organizationId: string;
  readonly actorId: string;
  readonly expectedVersion: number;
  readonly key: string;
  readonly #body: string;
  private constructor(organizationId: string, actorId: string, expectedVersion: number, configuration: OrganizationConfiguration, key: string) {
    this.organizationId = identifier(organizationId); this.actorId = identifier(actorId);
    this.expectedVersion = version(expectedVersion, true); this.key = identifier(key);
    this.#body = JSON.stringify({ version: this.expectedVersion, configuration: parseOrganizationConfiguration(configuration) });
    Object.freeze(this);
  }
  static review(organizationId: string, actorId: string, expectedVersion: number, configuration: OrganizationConfiguration, key = crypto.randomUUID()) {
    return new ConfigurationChangeIntent(organizationId, actorId, expectedVersion, configuration, key);
  }
  reviewedConfiguration(): OrganizationConfiguration {
    return parseOrganizationConfiguration((JSON.parse(this.#body) as { configuration: unknown }).configuration);
  }
  submit(signal: AbortSignal, sent: () => void = () => {}) {
    return boundedWorkRead(async bounded => {
      await actor(bounded, this.actorId); bounded.throwIfAborted();
      sent();
      const result = parseConfigurationRevision(await request(path(this.organizationId), this.actorId, bounded, {
        method: 'PATCH', headers: { 'Content-Type': 'application/json', 'Idempotency-Key': this.key }, body: this.#body,
      }), this.organizationId);
      if (result.actorId !== this.actorId || result.version !== this.expectedVersion + 1
        || JSON.stringify(result.configuration) !== JSON.stringify(this.reviewedConfiguration())) throw new WorkRequestError(503, null);
      await actor(bounded, this.actorId); bounded.throwIfAborted();
      // This may be a historical original receipt after a later change. The UI
      // must perform a fresh admitted GET before displaying current state.
      return result;
    }, signal);
  }
}
