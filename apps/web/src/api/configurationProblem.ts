// Only fixed server codes at their declared status may guide configuration UI.
// Values, arbitrary suffixes and server titles/details never cross this boundary.
export const configurationFields = ['LegalName', 'Jurisdiction', 'Timezone', 'CorporationIdentifier',
  'CivicAddress', 'LotCount', 'FiscalYearEnd', 'AgmCycleMonths', 'DepreciationReportCycleMonths',
  'ManagementCompanyName', 'EmergencyContacts', 'DefaultCategories', 'DefaultPriorities', 'Intake',
  'JurisdictionPolicies', 'configuration'] as const;
const statusByCode: Readonly<Record<string, number>> = Object.freeze({
  configuration_source_unavailable: 503,
  configuration_identifier_unavailable: 409,
  configuration_intake_unavailable: 400,
  idempotency_key_conflict: 409,
  idempotency_key_required: 400,
  invalid_configuration_cursor: 400,
  invalid_configuration_request: 400,
  ...Object.fromEntries(configurationFields.map(field => [`invalid_configuration_${field}`, 400])),
});
export function configurationProblemCode(value: unknown, status: number): string | undefined {
  return typeof value === 'string' && Object.hasOwn(statusByCode, value) && statusByCode[value] === status ? value : undefined;
}
