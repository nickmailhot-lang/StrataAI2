import { describe, expect, it } from 'vitest';
import { WorkRequestError } from '../../api/workManagement';
import { parseConfigurationHistory, parseConfigurationRevision, parseConfigurationView, parseOrganizationConfiguration } from './organizationConfiguration';

const organizationId = '11111111-1111-4111-8111-111111111111';
const otherId = '22222222-2222-4222-8222-222222222222';
const configuration = { legalName: 'Reviewed legal name', jurisdiction: 'BC', timezone: 'America/Vancouver',
  corporationIdentifier: 'ABC-123', civicAddress: 'First line\nSecond line', lotCount: 80,
  fiscalYearEndMonth: 2, fiscalYearEndDay: 29, agmCycleMonths: 12, depreciationReportCycleMonths: 36,
  insuranceRenewalDate: '2028-02-29', managementCompanyName: 'Reviewed manager',
  emergencyContacts: [{ name: 'Contact', email: null, phone: '+1 555 0100' }],
  defaultCategories: ['Maintenance'], defaultPriorities: ['Urgent'], intakeBoardId: otherId,
  intakeListId: '33333333-3333-4333-8333-333333333333',
  jurisdictionPolicies: [{ key: 'meeting.notice', value: 'Reviewed value', source: 'Reviewed source\nReference', notes: 'Explicit notes' }] };
function revision(version = 1) {
  return { organizationId, version, configuration: structuredClone(configuration), organizationName: 'Historical display name',
    organizationType: 'STRATA', organizationVersion: 2, actorId: otherId,
    eventId: `44444444-4444-4444-8444-${String(version).padStart(12, '0')}`, correlationId: 'public-reference',
    createdAt: '2026-10-10T13:00:00.0000001Z', updatedAt: '2026-10-10T13:00:00.0000002Z' };
}
function refusal(action: () => unknown) {
  try { action(); } catch (error) {
    expect(error).toBeInstanceOf(WorkRequestError);
    expect((error as WorkRequestError).status).toBe(503);
    expect((error as WorkRequestError).correlationId).toBeNull();
    expect((error as Error).message).not.toContain('Reviewed'); return;
  }
  throw new Error('Expected private response refusal');
}
describe('PRD-27 configuration response admission', () => {
  it('preserves every reviewed field and exact source timestamps without fabricating optional values', () => {
    expect(parseOrganizationConfiguration(configuration)).toEqual(configuration);
    expect(parseConfigurationRevision(revision(), organizationId)).toEqual(revision());
    const minimal = parseOrganizationConfiguration({ legalName: 'Legal', jurisdiction: 'BC', timezone: 'UTC' });
    expect(minimal.managementCompanyName).toBeNull(); expect(minimal.jurisdictionPolicies).toBeNull();
    expect(minimal.intakeBoardId).toBeNull(); expect(minimal.emergencyContacts).toBeNull();
  });
  it('copies nested private values rather than retaining mutable transport objects', () => {
    const input = revision(); const result = parseConfigurationRevision(input, organizationId);
    input.configuration.emergencyContacts[0].name = 'Changed'; input.configuration.jurisdictionPolicies[0].source = 'Changed';
    expect(result.configuration.emergencyContacts![0].name).toBe('Contact');
    expect(result.configuration.jurisdictionPolicies![0].source).toContain('Reviewed source');
  });
  it('admits only canonical empty state or a matching scoped positive revision', () => {
    expect(parseConfigurationView({ organizationId, version: 0, revision: null }, organizationId).revision).toBeNull();
    expect(parseConfigurationView({ organizationId, version: 1, revision: revision() }, organizationId).version).toBe(1);
    for (const value of [
      { organizationId: otherId, version: 0, revision: null }, { organizationId, version: 1, revision: null },
      { organizationId, version: 0, revision: revision() }, { organizationId, version: 2, revision: revision() },
      { organizationId, version: 1, revision: { ...revision(), organizationId: otherId } },
    ]) refusal(() => parseConfigurationView(value, organizationId));
  });
  it.each([
    { legalName: '' }, { legalName: 'x'.repeat(201) }, { jurisdiction: 'BC\u0000' }, { timezone: 'Unknown/Zone' },
    { lotCount: 0 }, { lotCount: 1.5 }, { lotCount: 1_000_001 }, { agmCycleMonths: 1201 },
    { fiscalYearEndMonth: 4, fiscalYearEndDay: 31 }, { fiscalYearEndDay: null },
    { insuranceRenewalDate: '2027-02-29' }, { insuranceRenewalDate: '0000-01-01' },
    { intakeBoardId: null }, { intakeListId: '00000000-0000-0000-0000-000000000000' },
    { emergencyContacts: [{ name: 'Contact', email: null, phone: null }] },
    { emergencyContacts: Array.from({ length: 33 }, () => ({ name: 'Contact', phone: '123' })) },
    { jurisdictionPolicies: [{ key: 'policy', value: 'Value', source: '' }] },
    { jurisdictionPolicies: [{ key: 'policy', value: 'Value', source: 'Source', secret: 'Unexpected' }] },
    { jurisdictionPolicies: [{ key: 'Policy', value: 'Value', source: 'Source' }, { key: 'policy', value: 'Value', source: 'Source' }] },
    { defaultPriorities: [null] }, { defaultCategories: Array.from({ length: 33 }, () => 'Category') },
    { confidentialExtra: 'Unexpected' },
  ])('refuses malformed field structures without exposing rejected values', patch => {
    refusal(() => parseOrganizationConfiguration({ ...configuration, ...patch }));
  });
  it('preserves Unicode labels which the server may treat as distinct', () => {
    expect(parseOrganizationConfiguration({ ...configuration, defaultCategories: ['ß', 'SS'] }).defaultCategories).toEqual(['ß', 'SS']);
  });
  it.each([
    { version: Number.MAX_SAFE_INTEGER + 1 }, { version: -1 }, { organizationVersion: 0 },
    { organizationType: 'UNKNOWN' }, { actorId: 'bad' }, { eventId: otherId + '/private' },
    { createdAt: '2026-02-30T13:00:00Z' }, { updatedAt: '2026-10-10T13:00:00.0000000Z' },
    { correlationId: 'reference\nprivate' }, { extra: 'Unexpected' },
  ])('refuses invalid source identity/version/history timestamps', patch => {
    refusal(() => parseConfigurationRevision({ ...revision(), ...patch }, organizationId));
  });
  it('validates bounded descending version pagination and exact cursor boundaries', () => {
    const items = Array.from({ length: 50 }, (_, index) => revision(100 - index));
    expect(parseConfigurationHistory({ organizationId, items, nextBeforeVersion: 51 }, organizationId, 101).items).toHaveLength(50);
    expect(parseConfigurationHistory({ organizationId, items: [revision(50)], nextBeforeVersion: null }, organizationId, 51).items[0].version).toBe(50);
    expect(parseConfigurationHistory({ organizationId, items: [], nextBeforeVersion: null }, organizationId).items).toEqual([]);
    for (const page of [
      { organizationId: otherId, items, nextBeforeVersion: 51 },
      { organizationId, items, nextBeforeVersion: 52 },
      { organizationId, items: [revision(50)], nextBeforeVersion: 50 },
      { organizationId, items: [revision(51)], nextBeforeVersion: null },
      { organizationId, items: [revision(50), revision(50)], nextBeforeVersion: null },
      { organizationId, items: [revision(49), revision(50)], nextBeforeVersion: null },
      { organizationId, items: Array.from({ length: 51 }, (_, index) => revision(100 - index)), nextBeforeVersion: null },
      { organizationId, items: [revision(50), { ...revision(49), eventId: revision(50).eventId }], nextBeforeVersion: null },
    ]) refusal(() => parseConfigurationHistory(page, organizationId, 51));
  });
});
