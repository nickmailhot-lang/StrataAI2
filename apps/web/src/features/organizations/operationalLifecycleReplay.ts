import { notificationInstant } from '../notifications/notificationInbox';
import { lifecycleUuid, type OperationalState } from './operationalLifecycle';

export type OperationalLifecycleFact = {
  eventId: string; organizationId: string; actorId: string; eventType: string; entityType: 'Organization';
  entityId: string; version: number; previousStatus: OperationalState; status: OperationalState;
  actingCapacity: 'ORGANIZATION_OWNER' | 'ORGANIZATION_ADMIN'; createdAt: string;
};
export type OperationalLifecycleReplay = {
  organizationId: string; actorId: string; status: OperationalState; version: number; afterVersion: number;
  nextVersion: number | null; pending: boolean; events: OperationalLifecycleFact[];
};
const record = (value: unknown): Record<string, unknown> | undefined =>
  value !== null && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, unknown> : undefined;
const keys = (value: Record<string, unknown>, expected: string) => Object.keys(value).sort().join(',') === expected;
const cursor = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0 && value < Number.MAX_SAFE_INTEGER;
const state = (value: unknown): value is OperationalState => ['ACTIVE', 'SUSPENDED', 'DEACTIVATED'].includes(value as string);
const eventTypes: Record<OperationalState, string> = {
  ACTIVE: 'ORGANIZATION_REACTIVATED', SUSPENDED: 'ORGANIZATION_SUSPENDED', DEACTIVATED: 'ORGANIZATION_DEACTIVATED',
};

// A source event is a historical fact. The parent snapshot, not the last fact,
// describes current lifecycle; neither grants permission to start operational work.
export function parseOperationalLifecycleReplay(input: unknown, organization: string, actor: string, afterVersion: number): OperationalLifecycleReplay | undefined {
  try {
    const page = record(input);
    if (!page || !keys(page, 'actorId,afterVersion,events,nextVersion,organizationId,pending,status,version')
      || !lifecycleUuid(organization) || !lifecycleUuid(actor) || !cursor(afterVersion)
      || page.organizationId !== organization || page.actorId !== actor || page.afterVersion !== afterVersion
      || !state(page.status) || !cursor(page.version) || page.version < 1 || afterVersion > page.version
      || typeof page.pending !== 'boolean' || !Array.isArray(page.events) || page.events.length > 50) return;
    const events: OperationalLifecycleFact[] = []; const ids = new Set<string>(); let position = afterVersion;
    for (const inputEvent of page.events) {
      const event = record(inputEvent);
      if (!event || !keys(event, 'actingCapacity,actorId,createdAt,entityId,entityType,eventId,eventType,organizationId,previousStatus,status,version')
        || !lifecycleUuid(event.eventId) || ids.has(event.eventId) || !lifecycleUuid(event.actorId)
        || event.organizationId !== organization || event.entityId !== organization || event.entityType !== 'Organization'
        || !cursor(event.version) || event.version <= position || event.version > page.version
        || !state(event.status) || !state(event.previousStatus) || event.status === event.previousStatus
        || event.eventType !== eventTypes[event.status]
        || !['ORGANIZATION_OWNER', 'ORGANIZATION_ADMIN'].includes(event.actingCapacity as string)
        || events.length > 0 && event.previousStatus !== events[events.length - 1].status
        || notificationInstant(event.createdAt).ticks % 10n !== 0n) return;
      ids.add(event.eventId); position = event.version;
      events.push({ ...event } as OperationalLifecycleFact);
    }
    if (page.nextVersion !== null && (!cursor(page.nextVersion) || events.length === 0
      || page.nextVersion !== position || page.nextVersion >= page.version)) return;
    if (page.pending && events.length > 0 && page.nextVersion === null) return;
    // A ready fact at the parent revision must describe that same snapshot.
    if (events.length > 0 && position === page.version && events[events.length - 1].status !== page.status) return;
    return { organizationId: organization, actorId: actor, status: page.status, version: page.version,
      afterVersion, nextVersion: page.nextVersion as number | null, pending: page.pending, events };
  } catch { return; }
}

export function parseOperationalLifecycleFrame(input: unknown, organization: string, actor: string, afterVersion: number) {
  const frame = record(input);
  if (!frame || !keys(frame, 'organizationId,page,userId') || frame.organizationId !== organization || frame.userId !== actor) return;
  return parseOperationalLifecycleReplay(frame.page, organization, actor, afterVersion);
}
