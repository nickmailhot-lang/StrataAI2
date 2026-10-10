import { describe, expect, it } from 'vitest';
import { parseConfigurationActivity } from './organizationConfigurationActivity';
const org = '11111111-1111-4111-8111-111111111111', actor = '22222222-2222-4222-8222-222222222222';
function row(version = 1) { return { eventId: `33333333-3333-4333-8333-${String(version).padStart(12, '0')}`, organizationId: org,
  actorId: actor, boardId: null, eventType: 'ORGANIZATION_CONFIGURATION_CHANGED', entityType: 'OrganizationConfiguration', entityId: org,
  version, correlationId: 'Original source', createdAt: '2026-10-10T13:00:00.0000001Z', metadata: {} }; }
const page = (items: unknown[] = [row()]) => ({ organizationId: org, items, nextCursor: null });
describe('PRD-27 configuration Activity response boundary', () => {
  it('retains precise original source references in immutable projections', () => {
    const parsed = parseConfigurationActivity(page(), org);
    expect(parsed.items[0]).toMatchObject({ actorId: actor, createdAt: '2026-10-10T13:00:00.0000001Z', version: 1 });
    expect(Object.isFrozen(parsed)).toBe(true); expect(Object.isFrozen(parsed.items)).toBe(true); expect(Object.isFrozen(parsed.items[0])).toBe(true);
    expect(parseConfigurationActivity(page([]), org).items).toEqual([]);
  });
  it.each([{ organizationId: actor }, { actorId: '' }, { boardId: actor }, { eventType: 'ORGANIZATION_UPDATED' },
    { metadata: { legalName: 'Private' } }, { legalName: 'Private' }, { version: Number.MAX_SAFE_INTEGER + 1 },
    { createdAt: 'not-a-clock' }, { correlationId: 'Bad\nsource' }])('refuses malformed or private source fields %j', change => {
    expect(() => parseConfigurationActivity(page([{ ...row(), ...change }]), org)).toThrow();
  });
  it('refuses duplicate identities, skipped revisions, foreign envelopes and unbounded or numeric continuations', () => {
    for (const value of [page([row(), row()]), page([row(3), row(1)]), { ...page(), organizationId: actor },
      { ...page(), nextCursor: '1' }, { ...page(), nextCursor: 'ciphertext' }, page(Array.from({ length: 51 }, (_, i) => row(51 - i)))])
      expect(() => parseConfigurationActivity(value, org)).toThrow();
    expect(parseConfigurationActivity({ ...page(Array.from({ length: 50 }, (_, i) => row(51 - i))), nextCursor: 'opaque_ciphertext' }, org).nextCursor).toBe('opaque_ciphertext');
  });
  it('binds a continuation to the last displayed revision and deduplicates UUID case variants', () => {
    expect(parseConfigurationActivity(page([row(2), row(1)]), org, 3).items).toHaveLength(2);
    expect(() => parseConfigurationActivity(page([row(2)]), org, 4)).toThrow();
    const id = 'abcdefab-abcd-4abc-8abc-abcdefabcdef';
    expect(() => parseConfigurationActivity(page([{ ...row(2), eventId: id }, { ...row(1), eventId: id.toUpperCase() }]), org)).toThrow();
  });
});
