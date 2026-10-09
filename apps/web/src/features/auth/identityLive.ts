import { HubConnectionBuilder, HttpTransportType, LogLevel } from '@microsoft/signalr';
import { validateIdentitySync } from './identitySync';

export function createIdentityConnection() {
  return new HubConnectionBuilder().withUrl('/me/live', {
    transport: HttpTransportType.WebSockets, withCredentials: true,
    headers: { 'X-StrataAI-Request': '1' }, timeout: 15_000,
  }).withAutomaticReconnect([0, 2000, 10_000, 30_000]).configureLogging(LogLevel.None).build();
}

export function watchIdentity<T extends { id: string; version: number }>(options: {
  subject: string; isProfile: (value: unknown) => value is T; invalidate: () => void;
  initialVersion?: number;
  connection?: ReturnType<typeof createIdentityConnection>;
}) {
  let connection: ReturnType<typeof createIdentityConnection>;
  try { connection = options.connection ?? createIdentityConnection(); }
  catch { options.invalidate(); return () => {}; } // Existing bounded HTTP recovery stays active.
  let disposed = false, generation = 0, attempt = 0;
  let cursor: number | undefined;
  // A protected read can already have admitted the snapshot revision. Keep
  // the initial handoff quiet only for that known revision, never for events.
  const initialVersion = typeof options.initialVersion === 'number' && Number.isSafeInteger(options.initialVersion)
    && options.initialVersion > 0 ? options.initialVersion : 0;
  let version = initialVersion;
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
    subscription = connection.stream<unknown>('Watch', cursor === undefined ? null : String(cursor)).subscribe({
      next: value => {
        if (disposed || active !== generation) return;
        const page = validateIdentitySync(value, cursor, options.isProfile, seen);
        if (!page || page.profile.id !== options.subject || page.profile.version < initialVersion) { fail(); return; }
        if (page.eventIds.length || page.profile.version > version) invalidate();
        version = Math.max(version, page.profile.version);
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
      subscribe();
    } catch { if (!disposed) { invalidate(); schedule(); } }
  }
  connection.onreconnecting(() => { if (!disposed) { ++generation; invalidate(); } });
  connection.onreconnected(() => { if (!disposed) { subscribe(); invalidate(); } });
  connection.onclose(() => { if (!disposed) { ++generation; invalidate(); schedule(); } });
  void start();
  return () => {
    disposed = true; ++generation;
    clearTimeout(retry); clearTimeout(refresh);
    subscription?.dispose();
    void connection.stop().catch(() => {});
  };
}
