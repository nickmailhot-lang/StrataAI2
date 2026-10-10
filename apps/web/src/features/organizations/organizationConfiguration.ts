import { WorkRequestError } from '../../api/workManagement';
import { notificationInstant, notificationUuid } from '../notifications/notificationInbox';
import { organizationTypeLabel } from './organizationTypes';

export type EmergencyContact = { name: string; email: string | null; phone: string | null };
export type JurisdictionPolicy = { key: string; value: string; source: string; notes: string | null };
export type OrganizationConfiguration = {
  legalName: string; jurisdiction: string; timezone: string;
  corporationIdentifier: string | null; civicAddress: string | null;
  lotCount: number | null; fiscalYearEndMonth: number | null; fiscalYearEndDay: number | null;
  agmCycleMonths: number | null; depreciationReportCycleMonths: number | null;
  insuranceRenewalDate: string | null; managementCompanyName: string | null;
  emergencyContacts: EmergencyContact[] | null;
  defaultCategories: string[] | null; defaultPriorities: string[] | null;
  intakeBoardId: string | null; intakeListId: string | null;
  jurisdictionPolicies: JurisdictionPolicy[] | null;
};
export type ConfigurationRevision = {
  organizationId: string; version: number; configuration: OrganizationConfiguration;
  organizationName: string; organizationType: string; organizationVersion: number;
  actorId: string; eventId: string; correlationId: string; createdAt: string; updatedAt: string;
};
export type ConfigurationView = { organizationId: string; version: number; revision: ConfigurationRevision | null };
export type ConfigurationHistory = { organizationId: string; items: ConfigurationRevision[]; nextBeforeVersion: number | null };

function unavailable(): never { throw new WorkRequestError(503, null); }
function object(value: unknown, keys: readonly string[]): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)
    || Object.keys(value).some(key => !keys.includes(key))) unavailable();
  return value as Record<string, unknown>;
}
function text(value: unknown, maximum: number, multiline = false): string {
  if (typeof value !== 'string' || !value.trim() || value.length > maximum
    || [...value].some(character => {
      const code = character.charCodeAt(0);
      return (code < 32 || code >= 127 && code <= 159)
        && !(multiline && (code === 9 || code === 10 || code === 13));
    })) unavailable();
  return value;
}
function optionalText(value: unknown, maximum: number, multiline = false): string | null {
  return value === null || value === undefined ? null : text(value, maximum, multiline);
}
function integer(value: unknown, maximum = Number.MAX_SAFE_INTEGER): number {
  if (!Number.isSafeInteger(value) || (value as number) < 1 || (value as number) > maximum) unavailable();
  return value as number;
}
function optionalInteger(value: unknown, maximum: number): number | null {
  return value === null || value === undefined ? null : integer(value, maximum);
}
function uuid(value: unknown): string {
  if (!notificationUuid(value)) unavailable();
  return value.toLowerCase();
}
function optionalUuid(value: unknown): string | null { return value == null ? null : uuid(value); }
function scope(value: unknown, organizationId: string): string {
  const id = uuid(value);
  if (id !== uuid(organizationId)) unavailable();
  return id;
}
function collection<T>(value: unknown, parse: (row: unknown) => T): T[] | null {
  if (value == null) return null;
  if (!Array.isArray(value) || value.length > 32) unavailable();
  return value.map(parse);
}
function labels(value: unknown): string[] | null {
  // Unicode duplicate semantics belong to the pinned server runtime; JavaScript
  // full casing can expand characters which .NET ordinal casing keeps distinct.
  return collection(value, row => text(row, 160));
}
function date(value: unknown): string | null {
  if (value == null) return null;
  if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(value) || value.startsWith('0000-')) unavailable();
  const milliseconds = Date.parse(`${value}T00:00:00Z`);
  if (!Number.isFinite(milliseconds) || new Date(milliseconds).toISOString().slice(0, 10) !== value) unavailable();
  return value;
}
const configurationKeys = ['legalName', 'jurisdiction', 'timezone', 'corporationIdentifier', 'civicAddress',
  'lotCount', 'fiscalYearEndMonth', 'fiscalYearEndDay', 'agmCycleMonths', 'depreciationReportCycleMonths',
  'insuranceRenewalDate', 'managementCompanyName', 'emergencyContacts', 'defaultCategories', 'defaultPriorities',
  'intakeBoardId', 'intakeListId', 'jurisdictionPolicies'] as const;

// The server owns legal/business validation. This boundary prevents malformed,
// foreign or unsupported private responses from becoming reviewable UI state.
export function parseOrganizationConfiguration(value: unknown): OrganizationConfiguration {
  const row = object(value, configurationKeys);
  const timezone = text(row.timezone, 128);
  try { new Intl.DateTimeFormat('en', { timeZone: timezone }); } catch { unavailable(); }
  const month = optionalInteger(row.fiscalYearEndMonth, 12);
  const day = optionalInteger(row.fiscalYearEndDay, 31);
  if ((month === null) !== (day === null)
    || month !== null && day! > new Date(Date.UTC(2000, month, 0)).getUTCDate()) unavailable();
  const intakeBoardId = optionalUuid(row.intakeBoardId); const intakeListId = optionalUuid(row.intakeListId);
  if (intakeListId && !intakeBoardId) unavailable();
  const emergencyContacts = collection(row.emergencyContacts, value => {
    const contact = object(value, ['name', 'email', 'phone']);
    const email = optionalText(contact.email, 320); const phone = optionalText(contact.phone, 80);
    if (!email && !phone) unavailable();
    return { name: text(contact.name, 160), email, phone };
  });
  const jurisdictionPolicies = collection(row.jurisdictionPolicies, value => {
    const policy = object(value, ['key', 'value', 'source', 'notes']); const key = text(policy.key, 64);
    if (!/^[A-Za-z0-9_.-]+$/.test(key)) unavailable();
    return { key, value: text(policy.value, 2000, true), source: text(policy.source, 1000, true), notes: optionalText(policy.notes, 2000, true) };
  });
  if (jurisdictionPolicies && new Set(jurisdictionPolicies.map(row => row.key.toUpperCase())).size !== jurisdictionPolicies.length) unavailable();
  return {
    legalName: text(row.legalName, 200), jurisdiction: text(row.jurisdiction, 120), timezone,
    corporationIdentifier: optionalText(row.corporationIdentifier, 120), civicAddress: optionalText(row.civicAddress, 1000, true),
    lotCount: optionalInteger(row.lotCount, 1_000_000), fiscalYearEndMonth: month, fiscalYearEndDay: day,
    agmCycleMonths: optionalInteger(row.agmCycleMonths, 1200), depreciationReportCycleMonths: optionalInteger(row.depreciationReportCycleMonths, 1200),
    insuranceRenewalDate: date(row.insuranceRenewalDate), managementCompanyName: optionalText(row.managementCompanyName, 200),
    emergencyContacts, defaultCategories: labels(row.defaultCategories), defaultPriorities: labels(row.defaultPriorities),
    intakeBoardId, intakeListId, jurisdictionPolicies,
  };
}

export function parseConfigurationRevision(value: unknown, organizationId: string): ConfigurationRevision {
  const row = object(value, ['organizationId', 'version', 'configuration', 'organizationName', 'organizationType',
    'organizationVersion', 'actorId', 'eventId', 'correlationId', 'createdAt', 'updatedAt']);
  let created; let updated;
  try { created = notificationInstant(row.createdAt); updated = notificationInstant(row.updatedAt); } catch { unavailable(); }
  if (updated.ticks < created.ticks || !organizationTypeLabel(row.organizationType)) unavailable();
  return { organizationId: scope(row.organizationId, organizationId), version: integer(row.version),
    configuration: parseOrganizationConfiguration(row.configuration), organizationName: text(row.organizationName, 160),
    organizationType: row.organizationType as string, organizationVersion: integer(row.organizationVersion),
    actorId: uuid(row.actorId), eventId: uuid(row.eventId), correlationId: text(row.correlationId, 256),
    createdAt: created.text, updatedAt: updated.text };
}

export function parseConfigurationView(value: unknown, organizationId: string): ConfigurationView {
  const row = object(value, ['organizationId', 'version', 'revision']); const id = scope(row.organizationId, organizationId);
  if (row.version === 0 && row.revision === null) return { organizationId: id, version: 0, revision: null };
  const version = integer(row.version); const revision = parseConfigurationRevision(row.revision, id);
  if (revision.version !== version) unavailable();
  return { organizationId: id, version, revision };
}

export function parseConfigurationHistory(value: unknown, organizationId: string, beforeVersion?: number): ConfigurationHistory {
  const row = object(value, ['organizationId', 'items', 'nextBeforeVersion']); const id = scope(row.organizationId, organizationId);
  if (beforeVersion !== undefined) integer(beforeVersion);
  if (!Array.isArray(row.items) || row.items.length > 50) unavailable();
  let previous = beforeVersion; const events = new Set<string>();
  const items = row.items.map(value => {
    const revision = parseConfigurationRevision(value, id);
    if (previous !== undefined && revision.version >= previous || events.has(revision.eventId)) unavailable();
    previous = revision.version; events.add(revision.eventId); return revision;
  });
  const next = row.nextBeforeVersion === null ? null : integer(row.nextBeforeVersion);
  if (next !== null && (items.length !== 50 || next !== items.at(-1)!.version)) unavailable();
  return { organizationId: id, items, nextBeforeVersion: next };
}
