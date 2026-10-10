import { WorkInputError } from '../../api/workManagement';
import { parseOrganizationConfiguration, type OrganizationConfiguration } from './organizationConfiguration';

export const configurationTextFields = [
  ['legalName', 'Legal name', 200], ['jurisdiction', 'Jurisdiction', 120], ['timezone', 'Organization timezone (IANA)', 128],
  ['corporationIdentifier', 'Corporation or registration identifier', 120], ['civicAddress', 'Civic address', 1000],
  ['managementCompanyName', 'Management company', 200], ['insuranceRenewalDate', 'Insurance renewal date', 10],
] as const;
export const configurationNumberFields = [
  ['lotCount', 'Lot count', 1_000_000], ['fiscalYearEndMonth', 'Fiscal year end month', 12],
  ['fiscalYearEndDay', 'Fiscal year end day', 31], ['agmCycleMonths', 'AGM cycle (months)', 1200],
  ['depreciationReportCycleMonths', 'Depreciation report cycle (months)', 1200],
] as const;
type TextKey = typeof configurationTextFields[number][0];
type NumberKey = typeof configurationNumberFields[number][0];
export type ConfigurationDraft = Record<TextKey | NumberKey | 'intakeBoardId' | 'intakeListId', string> & {
  emergencyContacts: { name: string; email: string; phone: string }[] | null;
  defaultCategories: string[] | null; defaultPriorities: string[] | null;
  jurisdictionPolicies: { key: string; value: string; source: string; notes: string }[] | null;
};
export function configurationDraft(value?: OrganizationConfiguration): ConfigurationDraft {
  const scalar = Object.fromEntries([...configurationTextFields, ...configurationNumberFields].map(([key]) => [key, value?.[key]?.toString() ?? '']));
  return { ...scalar, intakeBoardId: value?.intakeBoardId ?? '', intakeListId: value?.intakeListId ?? '',
    emergencyContacts: value?.emergencyContacts?.map(row => ({ name: row.name, email: row.email ?? '', phone: row.phone ?? '' })) ?? null,
    defaultCategories: value?.defaultCategories?.slice() ?? null, defaultPriorities: value?.defaultPriorities?.slice() ?? null,
    jurisdictionPolicies: value?.jurisdictionPolicies?.map(row => ({ ...row, notes: row.notes ?? '' })) ?? null } as ConfigurationDraft;
}
export function reviewedDraft(draft: ConfigurationDraft): OrganizationConfiguration {
  const numeric = Object.fromEntries(configurationNumberFields.map(([key, label, maximum]) => {
    const input = draft[key]; if (!input) return [key, null];
    if (!/^\d+$/.test(input) || !Number.isSafeInteger(Number(input)) || Number(input) < 1 || Number(input) > maximum)
      throw new WorkInputError(`${label} must be a whole number from 1 to ${maximum}.`);
    return [key, Number(input)];
  }));
  for (const [key, label] of configurationTextFields.slice(0, 3)) {
    if (!draft[key].trim()) throw new WorkInputError(`Enter ${label.toLowerCase()}.`);
  }
  const optional = (value: string) => value === '' ? null : value;
  try {
    return parseOrganizationConfiguration({ ...Object.fromEntries(configurationTextFields.map(([key]) => [key,
      ['legalName', 'jurisdiction', 'timezone'].includes(key) ? draft[key] : optional(draft[key])])), ...numeric,
      intakeBoardId: optional(draft.intakeBoardId), intakeListId: optional(draft.intakeListId),
      emergencyContacts: draft.emergencyContacts?.map(row => ({ name: row.name, email: optional(row.email), phone: optional(row.phone) })) ?? null,
      defaultCategories: draft.defaultCategories, defaultPriorities: draft.defaultPriorities,
      jurisdictionPolicies: draft.jurisdictionPolicies?.map(row => ({ ...row, notes: optional(row.notes) })) ?? null });
  } catch { throw new WorkInputError('Check the timezone, dates, contact details and policy sources before reviewing.'); }
}
