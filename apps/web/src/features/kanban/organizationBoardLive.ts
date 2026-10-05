import { HubConnectionBuilder, HttpTransportType, LogLevel } from '@microsoft/signalr';
import { validateOrganizationBoardSync } from './organizationBoardSync';

export function createOrganizationBoardConnection() {
  return new HubConnectionBuilder().withUrl('/organizations/live', {
    transport: HttpTransportType.WebSockets, withCredentials: true,
    headers: { 'X-StrataAI-Request': '1' }, timeout: 15_000,
  }).withAutomaticReconnect([0, 2000, 10_000, 30_000]).configureLogging(LogLevel.None).build();
}
export function watchOrganizationBoards(options: {
  organizationId: string; userId: string; invalidate: () => void; reset: () => void; unavailable: () => void;
  audience?: 'discovery';
  connection?: ReturnType<typeof createOrganizationBoardConnection>;
}) {
  let connection: ReturnType<typeof createOrganizationBoardConnection>;
  try { connection = options.connection ?? createOrganizationBoardConnection(); }
  catch { options.unavailable(); return () => {}; }
  let disposed = false, generation = 0, attempt = 0;
  let cursor: string | undefined;
  let subscription: { dispose(): void } | undefined;
  let retry: ReturnType<typeof setTimeout> | undefined;
  let refresh: ReturnType<typeof setTimeout> | undefined;
  const seen = new Map<string, string>();
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
    subscription = connection.stream<unknown>(options.audience === 'discovery' ? 'WatchBoards' : 'Watch', options.organizationId, cursor ?? null).subscribe({
      next: value => {
        if (disposed || active !== generation) return;
        const page = validateOrganizationBoardSync(value, options.organizationId, options.userId, seen);
        if (!page) { fail(); return; }
        // Ciphertext may change on every heartbeat. Only a canonical source or
        // reset invalidates the view; no numeric position or content hash is used.
        if (page.resetRequired) { seen.clear(); options.reset(); }
        if (page.eventIds.length) invalidate();
        cursor = page.cursor;
        for (const [id, fingerprint] of page.eventIds) seen.set(id, fingerprint);
        while (seen.size > 1000) seen.delete(seen.keys().next().value!);
        attempt = 0;
      },
      error: fail, complete: fail,
    });
    function fail() {
      queueMicrotask(() => {
        if (disposed || active !== generation) return;
        ++generation;
        options.unavailable();
        void connection.stop().catch(() => {}).finally(schedule);
      });
    }
  }
  async function start() {
    if (disposed) return;
    try {
      await connection.start();
      if (disposed) { await connection.stop(); return; }
      subscribe();
    } catch { if (!disposed) { options.unavailable(); schedule(); } }
  }
  connection.onreconnecting(() => { if (!disposed) { ++generation; options.unavailable(); } });
  connection.onreconnected(() => { if (!disposed) { subscribe(); invalidate(); } });
  connection.onclose(() => { if (!disposed) { ++generation; options.unavailable(); schedule(); } });
  void start();
  return () => {
    disposed = true; ++generation;
    clearTimeout(retry); clearTimeout(refresh);
    subscription?.dispose();
    void connection.stop().catch(() => {});
  };
}
