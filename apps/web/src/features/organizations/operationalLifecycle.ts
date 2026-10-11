export type OperationalState = 'ACTIVE' | 'SUSPENDED' | 'DEACTIVATED';
type Capacity = 'ORGANIZATION_OWNER' | 'ORGANIZATION_ADMIN';
export type LifecycleReview = { organizationId: string; actorId: string; status: OperationalState; version: number; updatedAt: string; actingCapacity: Capacity; name: string };
export type LifecycleIntent = { organizationId: string; actorId: string; key: string; status: OperationalState; previousStatus: OperationalState; version: number; actingCapacity: Capacity };
export type LifecycleAcknowledgment = { organizationId: string; actorId: string; eventId: string; actorLabel: string; actingCapacity: Capacity;
  previousStatus: OperationalState; status: OperationalState; version: number; correlationId: string; createdAt: string };
export const lifecycleUuid = (value: unknown): value is string => typeof value === 'string'
  && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/.test(value) && value !== '00000000-0000-0000-0000-000000000000';
const state = (value: unknown): value is OperationalState => ['ACTIVE', 'SUSPENDED', 'DEACTIVATED'].includes(value as string);
const capacity = (value: unknown): value is Capacity => value === 'ORGANIZATION_OWNER' || value === 'ORGANIZATION_ADMIN';
const revision = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value > 0 && value < Number.MAX_SAFE_INTEGER;
const sourceTime = (value: unknown): value is string => typeof value === 'string' && value.length <= 40
  && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(value) && Number.isFinite(Date.parse(value))
  && new Date(value).toISOString().slice(0,19) === value.slice(0,19);
const row = (value: unknown): Record<string, unknown> | undefined => value && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, unknown> : undefined;
const noControls = (value: string) => !Array.from(value).some(char => char.charCodeAt(0) < 32 || char.charCodeAt(0) === 127);
export function parseLifecycleReview(value: unknown, organization: string, actor: string): LifecycleReview | undefined {
  const item = row(value);
  if (item && Object.keys(item).length === 7 && lifecycleUuid(organization) && lifecycleUuid(actor)
    && item.organizationId === organization && item.actorId === actor && state(item.status)
    && revision(item.version) && sourceTime(item.updatedAt) && capacity(item.actingCapacity)
    && typeof item.name === 'string' && item.name.trim().length > 0 && item.name.length <= 160 && noControls(item.name)) return item as LifecycleReview;
}
export function parseLifecycleAcknowledgment(value: unknown, intent: LifecycleIntent): LifecycleAcknowledgment | undefined {
  const item = row(value);
  if (item && Object.keys(item).length === 10 && item.organizationId === intent.organizationId && item.actorId === intent.actorId
    && item.status === intent.status && item.previousStatus === intent.previousStatus && item.version === intent.version + 1
    && item.actingCapacity === intent.actingCapacity && lifecycleUuid(item.eventId) && sourceTime(item.createdAt) && !/\.\d{7}(?:Z|\+00:00)$/.test(item.createdAt)
    && typeof item.actorLabel === 'string' && item.actorLabel.trim().length > 0 && item.actorLabel.length <= 160 && noControls(item.actorLabel)
    && typeof item.correlationId === 'string' && item.correlationId.trim().length > 0 && item.correlationId.length <= 256 && noControls(item.correlationId)) return item as LifecycleAcknowledgment;
}
const cacheKey = (organization: string) => `strataai.operational-lifecycle.v1:${organization}`;
export function readLifecycleIntent(organization: string): LifecycleIntent | undefined {
  if (!lifecycleUuid(organization)) return;
  try {
    const raw = sessionStorage.getItem(cacheKey(organization)); if (!raw || raw.length > 1024) return;
    const item = row(JSON.parse(raw));
    if (item && Object.keys(item).length === 7 && item.organizationId === organization && lifecycleUuid(item.actorId)
      && lifecycleUuid(item.key) && state(item.status) && state(item.previousStatus) && item.status !== item.previousStatus
      && revision(item.version) && capacity(item.actingCapacity)) return item as LifecycleIntent;
  } catch { /* No submitted private content is stored. */ }
}
export function saveLifecycleIntent(intent: LifecycleIntent): boolean {
  try { sessionStorage.setItem(cacheKey(intent.organizationId), JSON.stringify(intent)); return true; } catch { return false; }
}
export function clearLifecycleIntent(organization: string) {
  try { sessionStorage.removeItem(cacheKey(organization)); } catch { /* Current in-memory intent still fences another command. */ }
}
export const lifecycleLabel = (value: OperationalState) => ({ ACTIVE: 'Active', SUSPENDED: 'Suspended', DEACTIVATED: 'Deactivated' })[value];
