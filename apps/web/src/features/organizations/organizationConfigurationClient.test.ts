import { afterEach, describe, expect, it, vi } from 'vitest';
import { parseOrganizationConfiguration } from './organizationConfiguration';
import { ConfigurationChangeIntent, readConfigurationIntakeBoard, readConfigurationIntakeBoards, readOrganizationConfiguration, readOrganizationConfigurationHistory } from './organizationConfigurationClient';
import { WorkRequestError } from '../../api/workManagement';

const organizationId = '11111111-1111-4111-8111-111111111111';
const actorId = '22222222-2222-4222-8222-222222222222';
const otherId = '33333333-3333-4333-8333-333333333333';
const key = '44444444-4444-4444-8444-444444444444';
const profile = { id: actorId, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'UTC' };
function configuration() {
  return parseOrganizationConfiguration({ legalName: 'Reviewed name', jurisdiction: 'BC', timezone: 'UTC',
    emergencyContacts: [{ name: 'Contact', phone: '123' }], jurisdictionPolicies: [{ key: 'policy', value: 'Value', source: 'Source' }] });
}
function revision(version = 1) {
  return { organizationId, version, configuration: configuration(), organizationName: 'Display name', organizationType: 'STRATA',
    organizationVersion: 1, actorId, eventId: '55555555-5555-4555-8555-555555555555', correlationId: 'fixed-reference',
    createdAt: '2026-10-10T13:00:00.0000001Z', updatedAt: '2026-10-10T13:00:00.0000002Z' };
}
function response(value: unknown, status = 200) { return new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } }); }
function sequence(...values: unknown[]) {
  const fetch = vi.fn(); values.forEach(value => fetch.mockResolvedValueOnce(response(value))); vi.stubGlobal('fetch', fetch); return fetch;
}
afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); });
describe('PRD-27 private account-bound configuration transport', () => {
  it('fences current reads with the same active account and expected-actor header', async () => {
    const fetch = sequence(profile, { organizationId, version: 1, revision: revision() }, profile);
    expect(await readOrganizationConfiguration(organizationId, new AbortController().signal)).toMatchObject({ actorId, view: { version: 1 } });
    expect(fetch.mock.calls.map(call => call[0])).toEqual(['/me', `/organizations/${organizationId}/configuration`, '/me']);
    expect(new Headers(fetch.mock.calls[1][1].headers).get('X-StrataAI-Expected-Actor')).toBe(actorId);
    expect(fetch.mock.calls[1][1].credentials).toBe('include');
  });
  it('refuses a current read after account replacement and never returns the protected view', async () => {
    sequence(profile, { organizationId, version: 1, revision: revision() }, { ...profile, id: otherId });
    await expect(readOrganizationConfiguration(organizationId, new AbortController().signal)).rejects.toMatchObject({ status: 401 });
  });
  it('does not issue a protected read for a replaced expected account', async () => {
    const fetch = sequence({ ...profile, id: otherId });
    await expect(readOrganizationConfiguration(organizationId, new AbortController().signal, actorId)).rejects.toMatchObject({ status: 401 });
    expect(fetch).toHaveBeenCalledTimes(1);
  });
  it('rejects foreign configuration before it can become current review state', async () => {
    const fetch = sequence(profile, { organizationId: otherId, version: 0, revision: null });
    await expect(readOrganizationConfiguration(organizationId, new AbortController().signal)).rejects.toMatchObject({ status: 503 });
    expect(fetch).toHaveBeenCalledTimes(2);
  });
  it('binds history pages to the reviewed actor, owning organization and exclusive version', async () => {
    const fetch = sequence(profile, { organizationId, items: [revision(4)], nextBeforeVersion: null }, profile);
    expect(await readOrganizationConfigurationHistory(organizationId, actorId, new AbortController().signal, 5)).toMatchObject({ items: [{ version: 4 }] });
    expect(fetch.mock.calls[1][0]).toBe(`/organizations/${organizationId}/configuration/history?beforeVersion=5`);
    expect(new Headers(fetch.mock.calls[1][1].headers).get('X-StrataAI-Expected-Actor')).toBe(actorId);
  });
  it('refuses history after account replacement without returning titles or counts', async () => {
    sequence(profile, { organizationId, items: [revision()], nextBeforeVersion: null }, { ...profile, id: otherId });
    await expect(readOrganizationConfigurationHistory(organizationId, actorId, new AbortController().signal)).rejects.toMatchObject({ status: 401 });
  });
  it('admits bounded Board discovery only between current private configuration authorization checks', async () => {
    const empty = { organizationId, version: 0, revision: null };
    const fetch = sequence(profile, empty, { organizationId, items: [{ id: otherId, name: 'Actual Board', version: 1 }], nextCursor: null }, empty, profile);
    expect(await readConfigurationIntakeBoards(organizationId, actorId, new AbortController().signal)).toEqual({ items: [{ id: otherId, name: 'Actual Board' }], nextCursor: null });
    expect(fetch.mock.calls.map(call => call[0])).toEqual(['/me', `/organizations/${organizationId}/configuration`,
      `/organizations/${organizationId}/boards/directory`, `/organizations/${organizationId}/configuration`, '/me']);
  });
  it('withdraws Board options if administrator authority disappears after directory discovery', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response({ organizationId, version: 0, revision: null }))
      .mockResolvedValueOnce(response({ organizationId, items: [{ id: otherId, name: 'Actual Board', version: 1 }], nextCursor: null }))
      .mockResolvedValueOnce(response({ code: 'organization_not_found' }, 404)); vi.stubGlobal('fetch', fetch);
    await expect(readConfigurationIntakeBoards(organizationId, actorId, new AbortController().signal)).rejects.toMatchObject({ status: 404 });
  });
  it.each([
    { organizationId: otherId, items: [], nextCursor: null },
    { organizationId, items: [{ id: otherId, name: 'Board', version: 0 }], nextCursor: null },
    { organizationId, items: [{ id: otherId, name: 'Board', version: 1 }], nextCursor: otherId },
    { organizationId, items: [], nextCursor: 'malformed' },
  ])('rejects foreign or malformed Board discovery pages before rendering relationship names', async page => {
    sequence(profile, { organizationId, version: 0, revision: null }, page);
    await expect(readConfigurationIntakeBoards(organizationId, actorId, new AbortController().signal)).rejects.toMatchObject({ status: 503 });
  });
  it('projects only the actual active owning Board/List relationship, discarding Card payloads', async () => {
    const empty = { organizationId, version: 0, revision: null };
    sequence(profile, empty, { board: { id: otherId, organizationId, name: 'Board', lifecycleState: 'active' }, access: { canAdminister: true },
      lists: [{ list: { id: key, organizationId, boardId: otherId, name: 'List', lifecycleState: 'active' }, cards: [{ title: 'Excluded private Card' }] }] }, empty, profile);
    const result = await readConfigurationIntakeBoard(organizationId, actorId, otherId, new AbortController().signal);
    expect(result).toEqual({ board: { id: otherId, name: 'Board' }, lists: [{ id: key, name: 'List' }] });
    expect(JSON.stringify(result)).not.toContain('Card');
  });
  it.each([
    { organizationId: otherId }, { boardId: key }, { lifecycleState: 'archived' }, { lifecycleState: 'ACTIVE' },
  ])('rejects substituted or archived List sources', async patch => {
    sequence(profile, { organizationId, version: 0, revision: null }, { board: { id: otherId, organizationId, name: 'Board', lifecycleState: 'active' },
      access: { canAdminister: true }, lists: [{ list: { id: key, organizationId, boardId: otherId, name: 'List', lifecycleState: 'active', ...patch } }] });
    await expect(readConfigurationIntakeBoard(organizationId, actorId, otherId, new AbortController().signal)).rejects.toMatchObject({ status: 503 });
  });
  it.each(['archived', 'ACTIVE', 'unknown'])('rejects unsupported or archived Board lifecycle %s', async lifecycleState => {
    sequence(profile, { organizationId, version: 0, revision: null }, { board: { id: otherId, organizationId, name: 'Board', lifecycleState },
      access: { canAdminister: true }, lists: [] });
    await expect(readConfigurationIntakeBoard(organizationId, actorId, otherId, new AbortController().signal)).rejects.toMatchObject({ status: 404 });
  });
  it('retains exact reviewed bytes and key across lost acknowledgment and ignores later caller mutation', async () => {
    const draft = configuration(); const intent = ConfigurationChangeIntent.review(organizationId, actorId, 0, draft, key);
    draft.legalName = 'Changed'; draft.emergencyContacts![0].name = 'Changed'; draft.jurisdictionPolicies![0].source = 'Changed';
    const copy = intent.reviewedConfiguration(); copy.legalName = 'Changed copy';
    const fetch = vi.fn().mockResolvedValueOnce(response(profile)).mockRejectedValueOnce(new TypeError('Transport lost'))
      .mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response(revision())).mockResolvedValueOnce(response(profile));
    vi.stubGlobal('fetch', fetch); const sent = vi.fn();
    await expect(intent.submit(new AbortController().signal, sent)).rejects.toBeInstanceOf(TypeError);
    expect(await intent.submit(new AbortController().signal, sent)).toMatchObject({ version: 1 });
    expect(sent).toHaveBeenCalledTimes(2); expect(Object.isFrozen(intent)).toBe(true);
    const commands = fetch.mock.calls.filter(call => call[1]?.method === 'PATCH'); expect(commands).toHaveLength(2);
    expect(commands[0][1].body).toBe(commands[1][1].body);
    expect(JSON.parse(commands[0][1].body)).toEqual({ version: 0, configuration: configuration() });
    for (const command of commands) {
      const headers = new Headers(command[1].headers);
      expect(headers.get('Idempotency-Key')).toBe(key); expect(headers.get('X-StrataAI-Expected-Actor')).toBe(actorId);
      expect(headers.get('X-StrataAI-Request')).toBe('1'); expect(headers.get('Content-Type')).toBe('application/json');
    }
  });
  it('does not send the reviewed change after account replacement', async () => {
    const fetch = sequence({ ...profile, id: otherId }); const sent = vi.fn();
    await expect(ConfigurationChangeIntent.review(organizationId, actorId, 0, configuration(), key)
      .submit(new AbortController().signal, sent)).rejects.toMatchObject({ status: 401 });
    expect(sent).not.toHaveBeenCalled(); expect(fetch).toHaveBeenCalledTimes(1);
  });
  it.each([
    { actorId: otherId }, { organizationId: otherId }, { version: 2 },
    { configuration: { ...configuration(), managementCompanyName: 'Different' } },
  ])('rejects mismatched acknowledgments without inventing command success', async patch => {
    sequence(profile, { ...revision(), ...patch });
    await expect(ConfigurationChangeIntent.review(organizationId, actorId, 0, configuration(), key)
      .submit(new AbortController().signal)).rejects.toMatchObject({ status: 503 });
  });
  it('accepts the original receipt after a newer admitted read without treating the old receipt as current state', async () => {
    sequence(profile, { organizationId, version: 3, revision: revision(3) }, profile, profile, revision(), profile);
    expect((await readOrganizationConfiguration(organizationId, new AbortController().signal, actorId)).view.version).toBe(3);
    const intent = ConfigurationChangeIntent.review(organizationId, actorId, 0, configuration(), key);
    expect((await intent.submit(new AbortController().signal)).version).toBe(1);
    expect(intent.expectedVersion).toBe(0); expect(intent.key).toBe(key);
  });
  it('withdraws a recovered receipt after the final account fence changes', async () => {
    sequence(profile, revision(), { ...profile, id: otherId });
    await expect(ConfigurationChangeIntent.review(organizationId, actorId, 0, configuration(), key)
      .submit(new AbortController().signal)).rejects.toMatchObject({ status: 401 });
  });
  it('refuses cancelled late responses before another request can start', async () => {
    let finish!: (value: Response) => void;
    const fetch = vi.fn().mockReturnValue(new Promise<Response>(resolve => { finish = resolve; })); vi.stubGlobal('fetch', fetch);
    const controller = new AbortController(); const result = readOrganizationConfiguration(organizationId, controller.signal);
    const rejected = expect(result).rejects.toMatchObject({ name: 'AbortError' });
    await vi.waitFor(() => expect(fetch).toHaveBeenCalledTimes(1)); controller.abort(); finish(response(profile)); await rejected;
    expect(fetch).toHaveBeenCalledTimes(1);
  });
  it('bounds a hung submitted request and keeps the original reviewed intent available for retry', async () => {
    vi.useFakeTimers(); const fetch = vi.fn().mockResolvedValueOnce(response(profile)).mockReturnValueOnce(new Promise(() => {}));
    vi.stubGlobal('fetch', fetch); const sent = vi.fn(); const intent = ConfigurationChangeIntent.review(organizationId, actorId, 0, configuration(), key);
    const failure = expect(intent.submit(new AbortController().signal, sent)).rejects.toMatchObject({ status: 503 });
    await vi.advanceTimersByTimeAsync(15_000); await failure;
    expect(sent).toHaveBeenCalledOnce(); expect(fetch).toHaveBeenCalledTimes(2);
    expect(intent.key).toBe(key); expect(intent.reviewedConfiguration()).toEqual(configuration());
  });
  it.each([
    [400, 'invalid_configuration_Timezone', 'invalid_configuration_Timezone'],
    [400, 'invalid_configuration_private-value', undefined],
    [409, 'invalid_configuration_Timezone', undefined],
    [409, 'configuration_identifier_unavailable', 'configuration_identifier_unavailable'],
    [409, 'idempotency_key_expired', 'idempotency_key_expired'],
    [409, 'idempotency_key_conflict', 'idempotency_key_conflict'],
    [409, 'version_conflict', 'version_conflict'],
    [400, 'configuration_intake_unavailable', 'configuration_intake_unavailable'],
  ])('admits fixed refusal codes at their declared status (%s)', async (status, code, expected) => {
    const fetch = vi.fn().mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response({ code, title: 'Private rejected value', detail: 'Private SQL' }, status));
    vi.stubGlobal('fetch', fetch);
    const failure = await ConfigurationChangeIntent.review(organizationId, actorId, 0, configuration(), key)
      .submit(new AbortController().signal).catch((error: unknown) => error);
    expect(failure).toBeInstanceOf(WorkRequestError); expect(failure).toMatchObject({ status });
    expect((failure as WorkRequestError).code).toBe(expected); expect((failure as Error).message).not.toContain('Private');
  });
});
