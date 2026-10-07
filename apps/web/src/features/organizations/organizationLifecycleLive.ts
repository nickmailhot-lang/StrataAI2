import { HubConnectionBuilder, HttpTransportType, LogLevel } from '@microsoft/signalr';
import { boundedWorkRead, workRequest, WorkRequestError } from '../../api/workManagement';
import { isNotificationProfile, notificationInstant, notificationUuid } from '../notifications/notificationInbox';

export type OrganizationLifecycleEvent = {
  eventId: string; eventType: 'ORGANIZATION_DELETED'; actorId: string; organizationId: string;
  boardId: null; entityType: 'Organization'; entityId: string; version: number; createdAt: string; metadata: Record<string, never>;
};
export type OrganizationLifecycleState = 'ACTIVE' | 'PENDING' | 'COMPLETED';
export function validateOrganizationLifecycle(input: unknown, organizationId: string, userId: string) {
  try {
    const frame = input as Record<string, unknown> | null;
    if (!frame || Object.keys(frame).sort().join(',') !== 'organizationId,page,userId'
      || frame.organizationId !== organizationId || frame.userId !== userId
      || !notificationUuid(organizationId) || !notificationUuid(userId)) return null;
    const page = frame.page as Record<string, unknown> | null;
    if (!page || Object.keys(page).sort().join(',') !== 'events,state'
      || !['ACTIVE', 'PENDING', 'COMPLETED'].includes(page.state as string) || !Array.isArray(page.events)
      || page.events.length !== (page.state === 'COMPLETED' ? 1 : 0)) return null;
    const event = page.events[0] as OrganizationLifecycleEvent | undefined;
    if (page.state === 'COMPLETED' && (!event || typeof event !== 'object')) return null;
    if (event && (Object.keys(event).sort().join(',') !== 'actorId,boardId,createdAt,entityId,entityType,eventId,eventType,metadata,organizationId,version'
      || !notificationUuid(event.eventId) || !notificationUuid(event.actorId) || event.eventType !== 'ORGANIZATION_DELETED'
      || event.organizationId !== organizationId || event.entityId !== organizationId || event.entityType !== 'Organization'
      || event.boardId !== null || !Number.isSafeInteger(event.version) || event.version < 3
      || !event.metadata || typeof event.metadata !== 'object' || Array.isArray(event.metadata) || Object.keys(event.metadata).length)) return null;
    const fingerprint = event ? `${event.eventId}/${event.actorId}/${event.version}/${notificationInstant(event.createdAt).ticks}` : undefined;
    return { state: page.state as OrganizationLifecycleState, event, fingerprint };
  } catch { return null; }
}
export function createOrganizationLifecycleConnection() {
  return new HubConnectionBuilder().withUrl('/organizations/live/lifecycle', {
    transport: HttpTransportType.WebSockets, withCredentials: true,
    headers: { 'X-StrataAI-Request': '1' }, timeout: 15_000,
  }).withAutomaticReconnect([0, 2000, 10_000, 30_000]).configureLogging(LogLevel.None).build();
}
export function watchOrganizationLifecycle(options: {
  organizationId: string; userId: string; update(state: OrganizationLifecycleState, event?: OrganizationLifecycleEvent): void;
  unavailable(): void; accountUnavailable(): void; connection?: ReturnType<typeof createOrganizationLifecycleConnection>;
}) {
  let connection: ReturnType<typeof createOrganizationLifecycleConnection>;
  try { connection = options.connection ?? createOrganizationLifecycleConnection(); }
  catch { options.unavailable(); return () => {}; }
  let disposed = false, generation = 0, attempt = 0, runtimeConfirmed = !!options.connection;
  let subscription: { dispose(): void } | undefined; let retry: ReturnType<typeof setTimeout> | undefined;
  let sourceFingerprint: string | undefined; let delivered: string | undefined;
  const lifetime = new AbortController();
  async function account() {
    const profile = await boundedWorkRead(signal => workRequest<unknown>('/me', { signal }), lifetime.signal);
    if (!isNotificationProfile(profile)) throw new Error('Unconfirmed account');
    if (profile.id !== options.userId) throw new WorkRequestError(401, null);
  }
  function schedule() {
    if (disposed || retry !== undefined) return;
    retry = setTimeout(() => { retry = undefined; void start(); }, [1000, 2000, 5000, 10_000, 30_000][Math.min(attempt++, 4)]);
  }
  function fail(error?: unknown, active = generation) {
    if (disposed || active !== generation) return;
    ++generation; delivered = undefined;
    if (error instanceof WorkRequestError && [401, 403].includes(error.status)) {
      disposed = true; lifetime.abort(); subscription?.dispose(); clearTimeout(retry);
      options.accountUnavailable(); void connection.stop().catch(() => {}); return;
    }
    options.unavailable(); void connection.stop().catch(() => {}).finally(schedule);
  }
  function subscribe() {
    if (disposed) return;
    clearTimeout(retry); retry = undefined; subscription?.dispose(); delivered = undefined;
    const active = ++generation; let delivery = Promise.resolve();
    subscription = connection.stream<unknown>('Watch', options.organizationId).subscribe({
      next: input => {
        delivery = delivery.then(async () => {
          if (disposed || active !== generation) return;
          const page = validateOrganizationLifecycle(input, options.organizationId, options.userId);
          if (!page || sourceFingerprint && page.fingerprint !== sourceFingerprint)
            throw new Error('Unconfirmed lifecycle source');
          await account(); if (disposed || active !== generation) return;
          await account(); if (disposed || active !== generation) return;
          if (page.fingerprint) sourceFingerprint = page.fingerprint;
          const key = page.fingerprint ?? page.state;
          if (key !== delivered) { delivered = key; options.update(page.state, page.event); }
          attempt = 0;
        }).catch(error => fail(error, active));
      },
      error: error => fail(error, active), complete: () => fail(undefined, active),
    });
  }
  async function start() {
    if (disposed) return;
    try {
      if (!runtimeConfirmed) {
        const runtime = await boundedWorkRead(signal => workRequest<unknown>('/api/runtime', { signal }), lifetime.signal);
        if (disposed) return;
        if (!runtime || typeof runtime !== 'object' || !('service' in runtime) || runtime.service !== 'strataai-api'
          || !('mode' in runtime) || runtime.mode !== 'production' && runtime.mode !== 'demo') throw new Error('Runtime unavailable');
        runtimeConfirmed = true;
      }
      await account(); if (disposed) return;
      await connection.start(); if (disposed) { await connection.stop(); return; }
      subscribe();
    } catch (error) { fail(error); }
  }
  connection.onreconnecting(() => {
    if (!disposed) { ++generation; delivered = undefined; options.unavailable(); }
  });
  connection.onreconnected(subscribe);
  connection.onclose(() => { if (!disposed) { ++generation; delivered = undefined; options.unavailable(); schedule(); } });
  void start();
  return () => {
    disposed = true; ++generation; lifetime.abort(); clearTimeout(retry); subscription?.dispose();
    void connection.stop().catch(() => {});
  };
}
