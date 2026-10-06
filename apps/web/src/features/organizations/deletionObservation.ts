export type DeletionIntent = { key: string; actor: string; version: number };
export type DeletionRecovery = DeletionIntent & { acknowledged: boolean };
export type DeletionObservation = { requestId: string; state: 'PENDING' | 'COMPLETED'; version: number; eventId: string | null; completedAt: string | null };
const uuid = (value: unknown): value is string => typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/.test(value) && value !== '00000000-0000-0000-0000-000000000000';
const cacheKey = (organization: string) => `strataai.organization-deletion.v1:${organization}`;
export function readDeletionRecovery(organization: string): DeletionRecovery | undefined {
  try {
    const raw = sessionStorage.getItem(cacheKey(organization)); if (!raw || raw.length > 256) return;
    const value: unknown = JSON.parse(raw);
    if (!value || typeof value !== 'object' || Array.isArray(value)) return;
    const row = value as Record<string, unknown>;
    if (Object.keys(row).length !== 4 || !uuid(row.key) || !uuid(row.actor) || !Number.isSafeInteger(row.version)
      || (row.version as number) < 1 || (row.version as number) > Number.MAX_SAFE_INTEGER - 2 || typeof row.acknowledged !== 'boolean') return;
    return row as DeletionRecovery;
  } catch { return; }
}
export function saveDeletionRecovery(organization: string, intent: DeletionIntent, acknowledged: boolean) {
  try { sessionStorage.setItem(cacheKey(organization), JSON.stringify({ key: intent.key, actor: intent.actor, version: intent.version, acknowledged })); }
  catch { /* Current-page recovery still works when browser storage is disabled. */ }
}
export function clearDeletionRecovery(organization: string) {
  try { sessionStorage.removeItem(cacheKey(organization)); } catch { /* No cached content to disclose. */ }
}
export function parseDeletionObservation(value: unknown, intent: DeletionIntent): DeletionObservation | undefined {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return;
  const row = value as Record<string, unknown>;
  if (Object.keys(row).length !== 5 || row.requestId !== intent.key || !Number.isSafeInteger(row.version)) return;
  if (row.state === 'PENDING' && row.version === intent.version + 1 && row.eventId === null && row.completedAt === null)
    return row as DeletionObservation;
  if (row.state === 'COMPLETED' && row.version === intent.version + 2 && uuid(row.eventId)
    && typeof row.completedAt === 'string' && row.completedAt.length <= 40
    && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$/.test(row.completedAt)
    && Number.isFinite(Date.parse(row.completedAt))) return row as DeletionObservation;
}
