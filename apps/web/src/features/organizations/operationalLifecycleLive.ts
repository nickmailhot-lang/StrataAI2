import { HubConnectionBuilder, HttpTransportType, LogLevel } from '@microsoft/signalr';
import { boundedWorkRead, workRequest, WorkRequestError } from '../../api/workManagement';
import { isNotificationProfile } from '../notifications/notificationInbox';
import { parseOperationalLifecycleFrame, type OperationalLifecycleReplay } from './operationalLifecycleReplay';

export function createOperationalLifecycleConnection() {
  return new HubConnectionBuilder().withUrl('/organizations/live/operational-lifecycle', {
    transport: HttpTransportType.WebSockets, withCredentials: true,
    headers: { 'X-StrataAI-Request': '1' }, timeout: 15_000,
  }).withAutomaticReconnect([0, 2000, 10_000, 30_000]).configureLogging(LogLevel.None).build();
}

export function watchOperationalLifecycle(options: {
  organizationId: string; actorId: string;
  // The consumer must freshly check its surface permission. A member's stream
  // permission does not confer administrator review or active work access.
  admit(page: OperationalLifecycleReplay, signal: AbortSignal): Promise<void>;
  update(page: OperationalLifecycleReplay): void; unavailable(): void; denied(status: number, switched: boolean): void;
  reconnected?(): void;
  connection?: ReturnType<typeof createOperationalLifecycleConnection>;
}) {
  let connection: ReturnType<typeof createOperationalLifecycleConnection>;
  try { connection = options.connection ?? createOperationalLifecycleConnection(); }
  catch { options.unavailable(); return () => {}; }
  let disposed = false, generation = 0, cursor = 0, attempt = 0;
  let runtimeConfirmed = !!options.connection, retry: ReturnType<typeof setTimeout> | undefined;
  let subscription: { dispose(): void } | undefined;
  let admission: AbortController | undefined;
  const lifetime = new AbortController();
  const active = (value: number) => !disposed && value === generation;
  async function account(signal: AbortSignal) {
    const profile = await boundedWorkRead(ct => workRequest<unknown>('/me', { signal: ct }), signal);
    if (!isNotificationProfile(profile)) throw new Error('Unconfirmed account');
    if (profile.id !== options.actorId) throw new WorkRequestError(401, null, 'account_replaced');
  }
  function invalidate() { ++generation; admission?.abort(); subscription?.dispose(); subscription = undefined; }
  function schedule() {
    if (disposed || retry !== undefined) return;
    retry = setTimeout(() => { retry = undefined; void start(); }, [1000, 2000, 5000, 10_000, 30_000][Math.min(attempt++, 4)]);
  }
  function fail(error: unknown, expected: number) {
    if (!active(expected)) return;
    invalidate();
    if (error instanceof WorkRequestError && [401, 403, 404].includes(error.status)) {
      disposed = true; lifetime.abort(); clearTimeout(retry); options.denied(error.status, error.code === 'account_replaced');
      void connection.stop().catch(() => {}); return;
    }
    options.unavailable(); void connection.stop().catch(() => {}).finally(schedule);
  }
  function subscribe() {
    if (disposed) return;
    invalidate(); clearTimeout(retry); retry = undefined;
    const expected = generation; const controller = new AbortController(); admission = controller;
    let delivery = Promise.resolve(), queued = 0;
    subscription = connection.stream<unknown>('Watch', options.organizationId, cursor).subscribe({
      next: input => {
        if (!active(expected)) return;
        if (++queued > 64) { fail(new Error('Replay backlog unavailable'), expected); return; }
        delivery = delivery.then(async () => {
          if (!active(expected)) return;
          const page = parseOperationalLifecycleFrame(input, options.organizationId, options.actorId, cursor);
          if (!page) throw new Error('Unconfirmed replay');
          await account(controller.signal); if (!active(expected)) return;
          await boundedWorkRead(signal => options.admit(page, signal), controller.signal); if (!active(expected)) return;
          await account(controller.signal); if (!active(expected)) return;
          options.update(page);
          if (!active(expected)) return;
          // Never advance to the parent revision across an unpublished source.
          if (page.events.length) cursor = page.events[page.events.length - 1].version;
          attempt = 0;
        }).catch(error => fail(error, expected)).finally(() => { --queued; });
      },
      error: error => fail(error, expected), complete: () => fail(undefined, expected),
    });
  }
  async function start() {
    const expected = generation;
    try {
      if (!runtimeConfirmed) {
        const runtime = await boundedWorkRead(signal => workRequest<unknown>('/api/runtime', { signal }), lifetime.signal);
        if (!active(expected)) return;
        if (!runtime || typeof runtime !== 'object' || !('service' in runtime) || runtime.service !== 'strataai-api'
          || !('mode' in runtime) || !['production', 'demo'].includes(runtime.mode as string)) throw new Error('Runtime unavailable');
        runtimeConfirmed = true;
      }
      await account(lifetime.signal); if (!active(expected)) return;
      await connection.start(); if (!active(expected)) { if (disposed) await connection.stop(); return; }
      subscribe();
    } catch (error) { fail(error, expected); }
  }
  connection.onreconnecting(() => { if (!disposed) { invalidate(); options.unavailable(); } });
  connection.onreconnected(() => { if (!disposed) { options.reconnected?.(); subscribe(); } });
  connection.onclose(() => { if (!disposed) { invalidate(); options.unavailable(); schedule(); } });
  void start();
  return () => { disposed = true; invalidate(); lifetime.abort(); clearTimeout(retry); void connection.stop().catch(() => {}); };
}
