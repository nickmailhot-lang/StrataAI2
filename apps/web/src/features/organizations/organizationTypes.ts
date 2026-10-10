export const organizationTypes = [
  ['STRATA', 'Strata'], ['HOA', 'Homeowners association'], ['CONDOMINIUM', 'Condominium'],
  ['COOPERATIVE', 'Cooperative'], ['PROPERTY_MANAGEMENT_COMPANY', 'Property management company'],
  ['GENERIC', 'General organization'],
] as const;

export function organizationTypeLabel(value: unknown): string | undefined {
  return organizationTypes.find(([type]) => type === value)?.[1];
}
