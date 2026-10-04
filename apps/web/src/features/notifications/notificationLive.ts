import { HubConnectionBuilder, HttpTransportType, LogLevel } from '@microsoft/signalr';
import { validateNotificationSync } from './notificationSync';

export function createNotificationConnection() {
  return new HubConnectionBuilder().withUrl('/notifications/live', {
    transport: HttpTransportType.WebSockets, withCredentials: true,
    headers: { 'X-StrataAI-Request': '1' }, timeout: 15_000,
  }).withAutomaticReconnect([0, 2000, 10_000, 30_000]).configureLogging(LogLevel.None).build();
}

export function watchNotifications(options: {
  organizationId: string; recipientId: string; invalidate: () => void;
  observe?: (kind: 'reconnect' | 'exception') => void;
  connection?: ReturnType<typeof createNotificationConnection>;
}) {
  let connection: ReturnType<typeof createNotificationConnection>;
  try { connection = options.connection ?? createNotificationConnection(); }
  catch { options.observe?.('exception'); options.invalidate(); return () => {}; } // Existing bounded HTTP recovery stays active.
  let disposed = false, generation = 0, attempt = 0;
  let cursor: string | undefined;

  const seen = new Set<string>();
  let subscription: { dispose(): void } | undefined;
  let retry: ReturnType<typeof setTimeout> | undefined;
  let refresh: ReturnType<typeof setTimeout> | undefined;
  function invalidate() {
    if (disposed || refresh !== undefined) return;
    refresh = setTimeout(() => { refresh = undefined; if (!disposed) options.invalidate(); }, 100);
  }
  function schedule() {
    if (disposed || retry !== undefined) return;
    retry = setTimeout(() => { retry = undefined; void start(); }, [1000, 2000, 5000, 10_000, 30_000][Math.min(attempt++, 4)]);
  }
  function subscribe() {
    if (disposed) return;
    clearTimeout(retry); retry = undefined;
    const active = ++generation;
    subscription?.dispose();
    subscription = connection.stream<unknown>('Watch', options.organizationId, cursor ?? null).subscribe({
      next: value => {
        if (disposed || active !== generation) return;
        const page = validateNotificationSync(value, options.organizationId, options.recipientId, cursor, seen);
        if (!page) { fail(); return; }
        if (cursor === undefined || page.resetRequired || page.cursor !== cursor || page.eventIds.length) invalidate();
        if (page.resetRequired) seen.clear();
        cursor = page.cursor;
        for (const id of page.eventIds) seen.add(id);
        while (seen.size > 1000) seen.delete(seen.values().next().value!);
        attempt = 0;
      },
      error: fail, complete: fail,
    });
    function fail() {
      // Allow the SDK's reconnect callback to fence the old stream first.
      queueMicrotask(() => {
        if (disposed || active !== generation) return;
        ++generation;
        options.observe?.('exception');
        invalidate();
        void connection.stop().catch(() => {}).finally(schedule);
      });
    }
  }
  async function start() {
    if (disposed) return;
    try {
      await connection.start();
      if (disposed) { await connection.stop(); return; }
      if (attempt > 0 && cursor !== undefined) options.observe?.('reconnect');
      subscribe();
    } catch { if (!disposed) { options.observe?.('exception'); invalidate(); schedule(); } }
  }
  connection.onreconnecting(() => { if (!disposed) { ++generation; invalidate(); } });
  connection.onreconnected(() => { if (!disposed) { options.observe?.('reconnect'); subscribe(); invalidate(); } });
  connection.onclose(() => { if (!disposed) { ++generation; invalidate(); schedule(); } });
  void start();
  return () => {
    disposed = true; ++generation;
    clearTimeout(retry); clearTimeout(refresh);
    subscription?.dispose();
    void connection.stop().catch(() => {});
  };
}
