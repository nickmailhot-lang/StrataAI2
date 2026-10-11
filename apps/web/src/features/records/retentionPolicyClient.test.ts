import { WorkRequestError } from '../../api/workManagement';
import { parsePolicyHistory, parsePolicyPage, parseRetentionPolicy, readRetentionPolicies, type RetentionPolicy } from './retentionPolicyClient';

const organization = '11111111-1111-4111-8111-111111111111';
const actor = '22222222-2222-4222-8222-222222222222';
const policy = '33333333-3333-4333-8333-333333333333';
const source: RetentionPolicy = { id: policy, organizationId: organization, version: 1, recordClass: 'document', category: 'contracts', jurisdiction: null,
  anchor: 'closed', retentionDays: 30, action: 'preserve', source: 'Reviewed source', notes: null, createdAt: '2026-10-10T12:00:00.0000001Z', updatedAt: '2026-10-10T12:00:00.0000002Z' };
afterEach(() => vi.unstubAllGlobals());
describe('PRD-70 private policy client sources', () => {
  it('preserves original fractional UTC clocks and explicit values', () => expect(parseRetentionPolicy(source, organization)).toEqual(source));
  it.each([
    { organizationId: actor }, { version: Number.MAX_SAFE_INTEGER + 1 }, { recordClass: 'card' }, { retentionDays: null, action: 'delete' },
    { source: 'Untrimmed ' }, { source: 'Control\u0000source' }, { category: 'category with space' }, { unknown: 'unsupported' },
    { updatedAt: '2026-10-10T12:00:00.0000000Z' }, { updatedAt: '2026-02-31T12:00:00Z' },
  ])('refuses malformed or foreign private policy source %j', change => expect(() => parseRetentionPolicy({ ...source, ...change }, organization)).toThrow(WorkRequestError));
  it('refuses duplicate identities, incorrect continuations and oversized directory pages', () => {
    expect(() => parsePolicyPage({ organizationId: organization, items: [source, source], nextAfter: null }, organization)).toThrow();
    expect(() => parsePolicyPage({ organizationId: organization, items: [source], nextAfter: policy }, organization)).toThrow();
    expect(() => parsePolicyPage({ organizationId: organization, items: Array(51).fill(source), nextAfter: null }, organization)).toThrow();
  });
  it('retains historical attribution and refuses gaps or rewritten source kinds', () => {
    const row = { policy: source, actorId: actor, actorLabel: 'Historical administrator', membershipId: actor, membershipVersion: 4, actingCapacity: 1, eventId: policy };
    const page = { organizationId: organization, policyId: policy, items: [row], nextBeforeVersion: null };
    expect(parsePolicyHistory(page, organization, policy).items[0].actorLabel).toBe('Historical administrator');
    expect(() => parsePolicyHistory({ ...page, items: [row, { ...row, policy: { ...source, version: 0 } }] }, organization, policy)).toThrow();
    expect(() => parsePolicyHistory({ ...page, items: [{ ...row, actingCapacity: 2 }] }, organization, policy)).toThrow();
    const newer = { ...row, policy: { ...source, version: 3 } };
    const older = { ...row, eventId: actor };
    expect(() => parsePolicyHistory({ ...page, items: [newer, older] }, organization, policy)).toThrow();
    expect(() => parsePolicyHistory({ ...page, items: [{ ...newer, policy: { ...newer.policy, version: 2 } },
      { ...older, policy: { ...source, recordClass: 'governance' } }] }, organization, policy)).toThrow();
  });
  it('withdraws a successful private read when owning authority disappears before final admission', async () => {
    vi.stubGlobal('fetch', vi.fn(async (url: string) => new Response(JSON.stringify(url === '/me'
      ? { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'UTC' }
      : url.includes('?after=') ? { code: 'organization_not_found', privateDiagnostic: 'Never disclose' }
        : { organizationId: organization, items: [source], nextAfter: null }), { status: url.includes('?after=') ? 404 : 200 })));
    await expect(readRetentionPolicies(organization, new AbortController().signal)).rejects.toMatchObject({ status: 404 });
  });
});
