import { describe, expect, it } from 'vitest';
import { configurationDraft, reviewedDraft } from './organizationConfigurationDraft';
import { parseOrganizationConfiguration } from './organizationConfiguration';
import { WorkInputError } from '../../api/workManagement';

describe('PRD-27 reviewed configuration draft', () => {
  it('requires explicit legal details rather than inferring them from display or account metadata', () => {
    const empty = configurationDraft();
    expect(empty.legalName).toBe(''); expect(empty.jurisdiction).toBe(''); expect(empty.timezone).toBe('');
    expect(() => reviewedDraft(empty)).toThrow(WorkInputError);
  });
  it('round-trips every configured field, including null and intentionally empty collections', () => {
    const data = parseOrganizationConfiguration({ legalName: 'Legal', jurisdiction: 'BC', timezone: 'UTC', corporationIdentifier: 'ABC',
      civicAddress: 'Line one\nLine two', lotCount: 100, fiscalYearEndMonth: 2, fiscalYearEndDay: 29, agmCycleMonths: 12,
      depreciationReportCycleMonths: 36, insuranceRenewalDate: '2028-02-29', managementCompanyName: 'Manager',
      emergencyContacts: [{ name: 'Contact', phone: '123' }], defaultCategories: [], defaultPriorities: null,
      intakeBoardId: '11111111-1111-4111-8111-111111111111', intakeListId: '22222222-2222-4222-8222-222222222222',
      jurisdictionPolicies: [{ key: 'policy', value: 'Reviewed value', source: 'Source', notes: 'Notes' }] });
    expect(reviewedDraft(configurationDraft(data))).toEqual(data);
    const copy = configurationDraft(data); copy.emergencyContacts![0].name = 'Changed';
    copy.jurisdictionPolicies![0].source = 'Changed'; expect(data.emergencyContacts![0].name).toBe('Contact');
    expect(data.jurisdictionPolicies![0].source).toBe('Source');
  });
  it.each(['0', '-1', '1.5', 'one', '1000001', '999999999999999999'])('refuses invalid lot input %s without erasing the draft', value => {
    const draft = configurationDraft(parseOrganizationConfiguration({ legalName: 'Legal', jurisdiction: 'BC', timezone: 'UTC' }));
    draft.lotCount = value; expect(() => reviewedDraft(draft)).toThrow('Lot count must be a whole number'); expect(draft.lotCount).toBe(value);
  });
  it('preserves incomplete fiscal/contact/policy work but refuses approval', () => {
    const source = configurationDraft(parseOrganizationConfiguration({ legalName: 'Legal', jurisdiction: 'BC', timezone: 'UTC' }));
    for (const patch of [ { fiscalYearEndMonth: '4', fiscalYearEndDay: '31' },
      { emergencyContacts: [{ name: 'Contact', email: '', phone: '' }] },
      { jurisdictionPolicies: [{ key: 'policy', value: 'Value', source: '', notes: '' }] } ]) {
      const draft = { ...source, ...patch }; expect(() => reviewedDraft(draft)).toThrow(WorkInputError); expect(draft).toMatchObject(patch);
    }
  });
});
