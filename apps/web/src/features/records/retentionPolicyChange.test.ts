import { WorkInputError } from '../../api/workManagement';
import { RetentionPolicyChangeIntent, type RetentionPolicyInput } from './retentionPolicyClient';
import { completeRetentionPolicyChange, forgetForeignRetentionPolicyChanges, forgetRetentionPolicyChanges,
  restoreRetentionPolicyChange, retainRetentionPolicyChange } from './retentionPolicyRecovery';

const organization = '11111111-1111-4111-8111-111111111111';
const actor = '22222222-2222-4222-8222-222222222222';
const policy = '33333333-3333-4333-8333-333333333333';
const key = '44444444-4444-4444-8444-444444444444';
const other = '55555555-5555-4555-8555-555555555555';
const input: RetentionPolicyInput = { recordClass: 'governance', category: 'decisions', jurisdiction: 'Reviewed jurisdiction',
  anchor: 'approved', retentionDays: null, action: 'preserve', source: 'Reviewed source\nReference', notes: null };
const receipt = { ...input, id: policy, organizationId: organization, version: 4,
  createdAt: '2026-10-10T12:00:00.0000001Z', updatedAt: '2026-10-10T12:00:00.0000002Z' };
const original = () => RetentionPolicyChangeIntent.review(organization, actor, policy, 3, input, key);
const storageKey = `strataai:retention-policy-change:v1:${actor}:${organization}`;
function transport(patch: (options: RequestInit) => Response | Promise<Response>, currentActor = actor) {
  const fetch = vi.fn(async (url: string, options: RequestInit = {}) => options.method === 'PATCH' ? patch(options)
    : new Response(JSON.stringify(url === '/me'
      ? { id: currentActor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'UTC' }
      : { organizationId: organization, items: [], nextAfter: null }), { status: 200 }));
  vi.stubGlobal('fetch', fetch); return fetch;
}
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); sessionStorage.clear(); vi.useRealTimers(); });
describe('PRD-70 reviewed policy identity and uncertain delivery', () => {
  it('restores original bytes after a lost acknowledgment, retrying the old revision and key despite later state', async () => {
    const proposed = { ...input }; const intent = RetentionPolicyChangeIntent.review(organization, actor, policy, 3, proposed, key);
    retainRetentionPolicyChange(sessionStorage, intent); proposed.source = 'A later draft';
    const bodies: string[] = []; let attempts = 0;
    const fetch = transport(options => {
      bodies.push(options.body as string);
      expect(new Headers(options.headers).get('Idempotency-Key')).toBe(key);
      expect(new Headers(options.headers).get('X-StrataAI-Expected-Actor')).toBe(actor);
      expect(new Headers(options.headers).get('X-StrataAI-Request')).toBe('1');
      if (++attempts === 1) throw new TypeError('Connection lost after server commit');
      return new Response(JSON.stringify(receipt));
    });
    await expect(intent.submit(new AbortController().signal)).rejects.toThrow();
    const restored = restoreRetentionPolicyChange(sessionStorage, actor, organization)!;
    const reviewed = restored.reviewedPolicy(); reviewed.source = 'Another draft';
    await expect(restored.submit(new AbortController().signal)).resolves.toEqual(receipt);
    expect(bodies).toHaveLength(2); expect(bodies[1]).toBe(bodies[0]);
    expect(JSON.parse(bodies[1])).toEqual({ version: 3, policy: input });
    expect(fetch.mock.calls.filter(([, options]) => options?.method === 'PATCH')).toHaveLength(2);
    completeRetentionPolicyChange(sessionStorage, restored); expect(sessionStorage.getItem(storageKey)).toBeNull();
  });
  it('times out a stalled delivery without replacing its retained original', async () => {
    vi.useFakeTimers(); const intent = original(); retainRetentionPolicyChange(sessionStorage, intent);
    const sent = vi.fn(); transport(() => new Promise<Response>(() => {}));
    const pending = expect(intent.submit(new AbortController().signal, sent)).rejects.toMatchObject({ status: 503 });
    await vi.advanceTimersByTimeAsync(15_000); await pending;
    expect(sent).toHaveBeenCalledTimes(1); expect(restoreRetentionPolicyChange(sessionStorage, actor, organization)!.recoveryRecord()).toEqual(intent.recoveryRecord());
  });
  it('refuses sending under a replacement account or withdrawn owning authority', async () => {
    const fetch = transport(() => new Response(JSON.stringify(receipt)), other);
    const sent = vi.fn(); await expect(original().submit(new AbortController().signal, sent)).rejects.toMatchObject({ status: 401 });
    expect(sent).not.toHaveBeenCalled(); expect(fetch).toHaveBeenCalledTimes(1);
    vi.stubGlobal('fetch', vi.fn(async (url: string) => new Response(JSON.stringify(url === '/me'
      ? { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'UTC' }
      : { code: 'organization_not_found' }), { status: url === '/me' ? 200 : 404 })));
    await expect(original().submit(new AbortController().signal, sent)).rejects.toMatchObject({ status: 404 });
    expect(sent).not.toHaveBeenCalled();
  });
  it.each([{ id: other }, { organizationId: other }, { version: 5 }, { source: 'Substituted source' }])(
    'refuses a substituted acknowledgment %j without releasing the original', async change => {
      const intent = original(); retainRetentionPolicyChange(sessionStorage, intent);
      transport(() => new Response(JSON.stringify({ ...receipt, ...change })));
      await expect(intent.submit(new AbortController().signal)).rejects.toMatchObject({ status: 503 });
      expect(restoreRetentionPolicyChange(sessionStorage, actor, organization)!.key).toBe(key);
    });
  it('withdraws the acknowledgment after authority is lost during the mutation', async () => {
    let written = false;
    vi.stubGlobal('fetch', vi.fn(async (url: string, options: RequestInit = {}) => {
      if (options.method === 'PATCH') { written = true; return new Response(JSON.stringify(receipt)); }
      return new Response(JSON.stringify(url === '/me'
        ? { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'UTC' }
        : { organizationId: organization, items: [], nextAfter: null }), { status: written && url !== '/me' ? 404 : 200 });
    }));
    await expect(original().submit(new AbortController().signal)).rejects.toMatchObject({ status: 404 });
  });
  it('withholds a completed mutation after the account changes before acknowledgment', async () => {
    let written = false;
    vi.stubGlobal('fetch', vi.fn(async (url: string, options: RequestInit = {}) => {
      if (options.method === 'PATCH') { written = true; return new Response(JSON.stringify(receipt)); }
      return new Response(JSON.stringify(url === '/me'
        ? { id: written ? other : actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'UTC' }
        : { organizationId: organization, items: [], nextAfter: null }));
    }));
    await expect(original().submit(new AbortController().signal)).rejects.toMatchObject({ status: 401 });
  });
  it('does not send an already canceled change', async () => {
    const controller = new AbortController(); controller.abort(); const sent = vi.fn();
    const fetch = transport(() => new Response(JSON.stringify(receipt)));
    await expect(original().submit(controller.signal, sent)).rejects.toMatchObject({ name: 'AbortError' });
    expect(sent).not.toHaveBeenCalled(); expect(fetch).not.toHaveBeenCalled();
  });
  it('returns only known conflict codes and safe references from private Problem responses', async () => {
    transport(() => new Response(JSON.stringify({ code: 'retention_policy_scope_unavailable', detail: 'Private source' }),
      { status: 409, headers: { 'X-Correlation-ID': 'Private source\tunsafe' } }));
    await expect(original().submit(new AbortController().signal)).rejects.toMatchObject({ status: 409, code: 'retention_policy_scope_unavailable', correlationId: null });
    transport(() => new Response(JSON.stringify({ code: 'Private source', detail: 'Private source' }), { status: 409 }));
    await expect(original().submit(new AbortController().signal)).rejects.toMatchObject({ status: 409, code: undefined });
    transport(() => new Response(JSON.stringify({ code: 'retention_policy_scope_unavailable' }), { status: 400 }));
    await expect(original().submit(new AbortController().signal)).rejects.toMatchObject({ status: 400, code: undefined });
  });
});
describe('PRD-70 bounded private policy recovery', () => {
  it('keeps one unresolved original per actor and Organization and never completes a different intent', () => {
    const intent = original(); retainRetentionPolicyChange(sessionStorage, intent);
    const replacement = RetentionPolicyChangeIntent.review(organization, actor, other, 0, input);
    expect(() => retainRetentionPolicyChange(sessionStorage, replacement)).toThrow('original');
    expect(() => completeRetentionPolicyChange(sessionStorage, replacement)).toThrow('original');
    expect(restoreRetentionPolicyChange(sessionStorage, actor, organization)!.policyId).toBe(policy);
  });
  it.each([
    (record: ReturnType<RetentionPolicyChangeIntent['recoveryRecord']>) => ({ ...record, actorId: other }),
    (record: ReturnType<RetentionPolicyChangeIntent['recoveryRecord']>) => ({ ...record, organizationId: other }),
    (record: ReturnType<RetentionPolicyChangeIntent['recoveryRecord']>) => ({ ...record, body: record.body + ' ' }),
    (record: ReturnType<RetentionPolicyChangeIntent['recoveryRecord']>) => ({ ...record, body: record.body.replace('"version":3', '"version":3,"version":3') }),
    (record: ReturnType<RetentionPolicyChangeIntent['recoveryRecord']>) => ({ ...record, extra: 'unsupported' }),
  ])('withholds corrupt recovery without replacing or deleting it', change => {
    const encoded = JSON.stringify(change(original().recoveryRecord())); sessionStorage.setItem(storageKey, encoded);
    expect(() => restoreRetentionPolicyChange(sessionStorage, actor, organization)).toThrow(WorkInputError);
    expect(() => retainRetentionPolicyChange(sessionStorage, original())).toThrow(WorkInputError);
    expect(sessionStorage.getItem(storageKey)).toBe(encoded);
  });
  it('fails closed on unwritable storage and full capacity without evicting unresolved work', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {});
    expect(() => retainRetentionPolicyChange(sessionStorage, original())).toThrow(); vi.restoreAllMocks();
    for (let index = 0; index < 32; index++) sessionStorage.setItem(`strataai:retention-policy-change:v1:existing:${index}`, 'retained');
    expect(() => retainRetentionPolicyChange(sessionStorage, original())).toThrow(); expect(sessionStorage.length).toBe(32);
  });
  it('removes private recovery on account withdrawal while preserving unrelated browser data', () => {
    retainRetentionPolicyChange(sessionStorage, original()); sessionStorage.setItem('unrelated', 'keep');
    forgetForeignRetentionPolicyChanges(sessionStorage, other); expect(sessionStorage.getItem(storageKey)).toBeNull();
    retainRetentionPolicyChange(sessionStorage, original()); forgetRetentionPolicyChanges();
    expect(sessionStorage.getItem(storageKey)).toBeNull(); expect(sessionStorage.getItem('unrelated')).toBe('keep');
  });
  it('rejects unsupported reviewed fields, indefinite deletion and unsafe revisions before a request', () => {
    expect(() => RetentionPolicyChangeIntent.review(organization, actor, policy, 3, { ...input, action: 'delete' })).toThrow(WorkInputError);
    expect(() => RetentionPolicyChangeIntent.review(organization, actor, policy, Number.MAX_SAFE_INTEGER, input)).toThrow(WorkInputError);
  });
});
