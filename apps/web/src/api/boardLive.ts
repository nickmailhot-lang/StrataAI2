import {
  HubConnectionBuilder,
  HttpTransportType,
  LogLevel,
} from "@microsoft/signalr";

export type LiveStatus = "connecting" | "live" | "recovering" | "polling";
type Change = {
  eventId: string;
  organizationId: string;
  boardId: string;
  sequence: string;
};
type Page = {
  cursor: string;
  hasMore: boolean;
  pending: boolean;
  resetRequired: boolean;
  events: Change[];
};
const decimal = (value: unknown): value is string =>
  typeof value === "string" &&
  /^[0-9]{1,19}$/.test(value) &&
  BigInt(value) <= 9223372036854775807n;
const guid = (value: unknown): value is string =>
  typeof value === "string" &&
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);

// Cursor advancement is atomic: a malformed, cross-scope or gapped page cannot
// make a later reconnect skip changes. Content is fetched through the scoped API.
export class BoardLiveCursor {
  cursor = "0";
  private seen = new Map<string, string>();
  constructor(
    private organizationId: string,
    private boardId: string,
  ) {}
  accept(input: unknown): {
    changed: boolean;
    reset: boolean;
    pending: boolean;
  } {
    if (!input || typeof input !== "object")
      throw new Error("Invalid live page.");
    const page = input as Page;
    if (
      !decimal(page.cursor) ||
      typeof page.hasMore !== "boolean" ||
      typeof page.pending !== "boolean" ||
      typeof page.resetRequired !== "boolean" ||
      !Array.isArray(page.events) ||
      page.events.length > 100
    )
      throw new Error("Invalid live page.");
    if (page.resetRequired) {
      if (
        page.cursor !== "0" ||
        page.events.length ||
        page.hasMore ||
        page.pending
      )
        throw new Error("Invalid live page.");
      this.cursor = "0";
      this.seen.clear();
      return { changed: false, reset: true, pending: false };
    }
    let after = BigInt(this.cursor);
    const added: Change[] = [];
    for (const change of page.events) {
      if (
        !change ||
        typeof change !== "object" ||
        !guid(change.eventId) ||
        !decimal(change.sequence) ||
        change.organizationId !== this.organizationId ||
        change.boardId !== this.boardId
      )
        throw new Error("Invalid live page.");
      const sequence = BigInt(change.sequence);
      if (sequence <= after) {
        if (this.seen.get(change.eventId) !== change.sequence)
          throw new Error("Invalid live page.");
      } else {
        if (
          sequence !== after + 1n ||
          this.seen.has(change.eventId) ||
          added.some((item) => item.eventId === change.eventId)
        )
          throw new Error("Invalid live page.");
        after = sequence;
        added.push(change);
      }
    }
    if (
      (BigInt(page.cursor) !== after &&
        !(added.length === 0 && BigInt(page.cursor) <= after)) ||
      (page.hasMore && (page.pending || page.events.length !== 100))
    )
      throw new Error("Invalid live page.");
    for (const change of added) this.seen.set(change.eventId, change.sequence);
    while (this.seen.size > 1024)
      this.seen.delete(this.seen.keys().next().value!);
    this.cursor = after.toString();
    return { changed: added.length > 0, reset: false, pending: page.pending };
  }
}

export function createBoardConnection() {
  return new HubConnectionBuilder()
    .withUrl("/boards/live", {
      transport: HttpTransportType.WebSockets,
      withCredentials: true,
      headers: { "X-StrataAI-Request": "1" },
      timeout: 15_000,
    })
    .withAutomaticReconnect([0, 2000, 10_000, 30_000])
    .configureLogging(LogLevel.None)
    .build();
}

// The SDK owns transport framing/heartbeats. This controller owns durable cursor
// recovery, initial-start retries, scoped disposal and degraded snapshot polling.
export function watchBoard(options: {
  organizationId: string;
  boardId: string;
  invalidate: () => void;
  status: (value: LiveStatus) => void;
  reconnected?: () => void;
  connection?: ReturnType<typeof createBoardConnection>;
}) {
  let connection: ReturnType<typeof createBoardConnection>;
  try {
    connection = options.connection ?? createBoardConnection();
  } catch {
    // An unsupported browser transport must not make the board unusable.
    options.status("polling");
    const first = setTimeout(options.invalidate, 100);
    const poll = setInterval(options.invalidate, 10_000);
    return () => {
      clearTimeout(first);
      clearInterval(poll);
    };
  }
  const cursor = new BoardLiveCursor(options.organizationId, options.boardId);
  let disposed = false,
    connected = false,
    pending = false,
    resetting = false,
    attempt = 0,
    generation = 0;
  let subscription: { dispose(): void } | undefined;
  let retry: ReturnType<typeof setTimeout> | undefined;
  let refresh: ReturnType<typeof setTimeout> | undefined;
  let established = false, transportRecovery = false;
  const invalidate = () => {
    if (disposed || refresh !== undefined) return;
    refresh = setTimeout(() => {
      refresh = undefined;
      if (!disposed) options.invalidate();
    }, 100);
  };
  const report = (value: LiveStatus) => {
    if (!disposed) options.status(value);
  };
  const failed = () => {
    if (established) transportRecovery = true;
    connected = false;
    report("polling");
    invalidate();
  };
  function schedule() {
    if (disposed || retry !== undefined) return;
    const delay = [1000, 2000, 5000, 10_000, 30_000][Math.min(attempt++, 4)];
    retry = setTimeout(() => {
      retry = undefined;
      void start();
    }, delay);
  }
  function subscribe() {
    if (disposed) return;
    clearTimeout(retry);
    retry = undefined;
    const active = ++generation;
    subscription?.dispose();
    connected = true;
    report("recovering");
    subscription = connection
      .stream<unknown>("Watch", options.boardId, cursor.cursor)
      .subscribe({
        next: (page) => {
          if (disposed || active !== generation) return;
          try {
            const accepted = cursor.accept(page);
            attempt = 0;
            if (
              accepted.changed ||
              (accepted.reset && !resetting) ||
              (accepted.pending && !pending)
            )
              invalidate();
            pending = accepted.pending;
            resetting = accepted.reset;
            if (!pending && !resetting) {
              const recovered = established && transportRecovery;
              established = true; transportRecovery = false;
              if (recovered) { try { options.reconnected?.(); } catch { /* Observers cannot break durable recovery. */ } }
            }
            report(pending || resetting ? "recovering" : "live");
          } catch {
            failStream();
          }
        },
        error: () => {
          if (!disposed && active === generation) failStream();
        },
        complete: () => {
          if (!disposed && active === generation) failStream();
        },
      });
    function failStream() {
      if (disposed || active !== generation) return;
      // The SDK cancels streams before entering its reconnect lifecycle. Let
      // onreconnecting invalidate this generation before deciding to restart.
      queueMicrotask(() => {
        if (disposed || active !== generation) return;
        ++generation;
        failed();
        void connection
          .stop()
          .catch(() => {})
          .finally(schedule);
      });
    }
  }
  async function start() {
    if (disposed) return;
    try {
      await connection.start();
      if (disposed) {
        await connection.stop();
        return;
      }
      subscribe();
    } catch {
      if (!disposed) {
        failed();
        schedule();
      }
    }
  }
  connection.onreconnecting(() => {
    if (!disposed) {
      ++generation;
      failed();
      report("recovering");
    }
  });
  connection.onreconnected(() => {
    if (!disposed) {
      if (established) transportRecovery = true;
      subscribe();
      invalidate();
    }
  });
  connection.onclose(() => {
    if (!disposed) {
      ++generation;
      failed();
      schedule();
    }
  });
  const poll = setInterval(() => {
    if (!connected || pending || resetting) invalidate();
  }, 10_000);
  report("connecting");
  void start();
  return () => {
    disposed = true;
    ++generation;
    clearInterval(poll);
    clearTimeout(retry);
    clearTimeout(refresh);
    subscription?.dispose();
    void connection.stop().catch(() => {});
  };
}
