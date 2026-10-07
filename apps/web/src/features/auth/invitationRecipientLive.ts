import { HubConnectionBuilder, HttpTransportType, LogLevel } from '@microsoft/signalr';
import { validateInvitationRecipientSync } from './invitationRecipientSync';

export function createInvitationRecipientConnection() {
  return new HubConnectionBuilder().withUrl('/invitations/live', {
    transport: HttpTransportType.WebSockets, withCredentials: true,
    headers: { 'X-StrataAI-Request': '1' }, timeout: 15_000,
  }).withAutomaticReconnect([0, 2000, 10_000, 30_000]).configureLogging(LogLevel.None).build();
}

export type InvitationRecipientInvalidation = 'reset' | 'change' | 'reconnecting' | 'unavailable';
export function watchInvitationRecipient(options: {
  invalidate: (reason: InvitationRecipientInvalidation) => void;
  connection?: ReturnType<typeof createInvitationRecipientConnection>;
}) {
  let connection: ReturnType<typeof createInvitationRecipientConnection>;
  try { connection = options.connection ?? createInvitationRecipientConnection(); }
  catch { options.invalidate('unavailable'); return () => {}; }
  let disposed = false, generation = 0, attempt = 0;
  let cursor: string | undefined; let lastSequence: bigint | undefined;
  const seen = new Set<string>();
  let subscription: { dispose(): void } | undefined;
  let retry: ReturnType<typeof setTimeout> | undefined;
  function schedule() {
    if (disposed || retry !== undefined) return;
    retry = setTimeout(() => { retry = undefined; void start(); }, [1000, 2000, 5000, 10_000, 30_000][Math.min(attempt++, 4)]);
  }
  function subscribe() {
    if (disposed) return;
    clearTimeout(retry); retry = undefined;
    const active = ++generation; subscription?.dispose();
    subscription = connection.stream<unknown>('Watch', cursor ?? null).subscribe({
      next: value => {
        if (disposed || active !== generation) return;
        const page = validateInvitationRecipientSync(value, cursor, lastSequence, seen);
        if (!page) { fail(); return; }
        if (page.resetRequired) seen.clear();
        cursor = page.cursor; lastSequence = page.lastSequence;
        for (const id of page.eventIds) seen.add(id);
        while (seen.size > 1000) seen.delete(seen.values().next().value!);
        attempt = 0;
        // Invalidate synchronously: consumers must withdraw old consent before
        // beginning protected discovery. Heartbeat token rotation is neutral.
        if (page.resetRequired || page.eventIds.length) options.invalidate(page.resetRequired ? 'reset' : 'change');
      },
      error: fail, complete: fail,
    });
    function fail() {
      if (disposed || active !== generation) return;
      ++generation; options.invalidate('unavailable');
      queueMicrotask(() => {
        if (disposed || generation !== active + 1) return;
        subscription?.dispose();
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
    } catch { if (!disposed) { options.invalidate('unavailable'); schedule(); } }
  }
  connection.onreconnecting(() => { if (!disposed) { ++generation; options.invalidate('reconnecting'); } });
  connection.onreconnected(() => { if (!disposed) subscribe(); });
  connection.onclose(() => { if (!disposed) { ++generation; options.invalidate('unavailable'); schedule(); } });
  void start();
  return () => {
    disposed = true; ++generation; clearTimeout(retry); subscription?.dispose();
    void connection.stop().catch(() => {});
  };
}
