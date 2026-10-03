import { HubConnectionBuilder } from "@microsoft/signalr";
import {
  BoardLiveCursor,
  watchBoard,
  type createBoardConnection,
} from "./boardLive";

const org = "11111111-1111-1111-1111-111111111111";
const board = "22222222-2222-2222-2222-222222222222";
const id = (sequence: string) =>
  `33333333-3333-3333-3333-${sequence.padStart(12, "0")}`;
const event = (sequence: string) => ({
  eventId: id(sequence),
  organizationId: org,
  boardId: board,
  sequence,
});
const page = (cursor: string, events = [event(cursor)]) => ({
  cursor,
  events,
  hasMore: false,
  pending: false,
  resetRequired: false,
});

describe("PRD-22 durable browser cursor", () => {
  it("deduplicates current and older pages without rewinding", () => {
    const cursor = new BoardLiveCursor(org, board);
    expect(cursor.accept(page("1")).changed).toBe(true);
    expect(cursor.accept(page("2")).changed).toBe(true);
    expect(cursor.accept(page("1")).changed).toBe(false);
    expect(cursor.cursor).toBe("2");
  });
  it("rejects a gapped batch atomically then accepts the original sequence", () => {
    const cursor = new BoardLiveCursor(org, board);
    expect(() => cursor.accept(page("3", [event("1"), event("3")]))).toThrow();
    expect(cursor.cursor).toBe("0");
    expect(cursor.accept(page("1")).changed).toBe(true);
  });
  it("rejects foreign scope and reused identities without advancing", () => {
    const cursor = new BoardLiveCursor(org, board);
    cursor.accept(page("1"));
    expect(() =>
      cursor.accept(page("2", [{ ...event("2"), organizationId: board }])),
    ).toThrow();
    expect(() =>
      cursor.accept(page("2", [{ ...event("2"), eventId: id("1") }])),
    ).toThrow();
    expect(cursor.cursor).toBe("1");
  });
  it("preserves bigint precision and rejects malformed or overflowing cursors", () => {
    const cursor = new BoardLiveCursor(org, board);
    cursor.cursor = "9007199254740992";
    const change = { ...event("1"), sequence: "9007199254740993" };
    cursor.accept(page("9007199254740993", [change]));
    expect(cursor.cursor).toBe("9007199254740993");
    for (const invalid of ["9223372036854775808", "-1", "1.5", "", "1e3"])
      expect(() => cursor.accept(page(invalid, []))).toThrow();
  });
  it("handles bounded pending/reset recovery without accepting a reset payload", () => {
    const cursor = new BoardLiveCursor(org, board);
    expect(cursor.accept({ ...page("0", []), pending: true }).pending).toBe(
      true,
    );
    cursor.accept(page("1"));
    expect(() =>
      cursor.accept({ ...page("0"), resetRequired: true }),
    ).toThrow();
    expect(cursor.cursor).toBe("1");
    expect(cursor.accept({ ...page("0", []), resetRequired: true }).reset).toBe(
      true,
    );
    expect(cursor.cursor).toBe("0");
    expect(() =>
      cursor.accept(
        page(
          "101",
          Array.from({ length: 101 }, (_, index) => event(String(index + 1))),
        ),
      ),
    ).toThrow();
  });
});

function fakeConnection() {
  type Observer = {
    next(value: unknown): void;
    error(error: Error): void;
    complete(): void;
  };
  let observer: Observer | undefined;
  let reconnecting = () => {},
    reconnected = () => {},
    closed = () => {};
  const subscription = { dispose: vi.fn() };
  const connection = {
    start: vi.fn(async () => {}),
    stop: vi.fn(async () => {}),
    stream: vi.fn(() => ({
      subscribe: vi.fn((value: Observer) => {
        observer = value;
        return subscription;
      }),
    })),
    onreconnecting: (callback: () => void) => {
      reconnecting = callback;
    },
    onreconnected: (callback: () => void) => {
      reconnected = callback;
    },
    onclose: (callback: () => void) => {
      closed = callback;
    },
  };
  return {
    connection,
    next: (value: unknown) => observer!.next(value),
    error: () => observer!.error(new Error("transport closed")),
    reconnecting: () => reconnecting(),
    reconnected: () => reconnected(),
    close: () => closed(),
    subscription,
  };
}

describe("PRD-22 browser stream recovery", () => {
  beforeEach(() => vi.useFakeTimers());
  it("keeps snapshot fallback usable when transport construction fails", async () => {
    const failed = vi
      .spyOn(HubConnectionBuilder.prototype, "build")
      .mockImplementationOnce(() => {
        throw new Error("private constructor detail");
      });
    const invalidate = vi.fn(),
      status = vi.fn();
    const stop = watchBoard({
      organizationId: org,
      boardId: board,
      invalidate,
      status,
    });
    await vi.advanceTimersByTimeAsync(10_100);
    expect(status).toHaveBeenCalledWith("polling");
    expect(invalidate).toHaveBeenCalledTimes(2);
    stop();
    await vi.advanceTimersByTimeAsync(20_000);
    expect(invalidate).toHaveBeenCalledTimes(2);
    failed.mockRestore();
  });
  afterEach(() => vi.useRealTimers());
  function mount(fake = fakeConnection()) {
    const invalidate = vi.fn(),
      status = vi.fn(), reconnected = vi.fn();
    const stop = watchBoard({
      organizationId: org,
      boardId: board,
      invalidate,
      status,
      reconnected,
      connection: fake.connection as unknown as ReturnType<
        typeof createBoardConnection
      >,
    });
    return { ...fake, stop, invalidate, status, recovered: reconnected };
  }
  it('reports transport recovery only after an established stream resumes validated non-pending pages', async () => {
    const live = mount(); await vi.advanceTimersByTimeAsync(0);
    live.next(page('1')); expect(live.recovered).not.toHaveBeenCalled();
    live.next({ ...page('1'), pending: true }); live.next(page('1'));
    expect(live.recovered).not.toHaveBeenCalled();
    live.reconnecting(); live.reconnected();
    live.next({ ...page('1'), pending: true }); expect(live.recovered).not.toHaveBeenCalled();
    live.next(page('1')); live.next(page('1')); expect(live.recovered).toHaveBeenCalledOnce();
    live.recovered.mockImplementation(() => { throw new Error('Observer failure'); });
    live.reconnecting(); live.reconnected(); live.next(page('1'));
    expect(live.recovered).toHaveBeenCalledTimes(2); expect(live.status).toHaveBeenLastCalledWith('live');
    live.stop(); live.reconnected(); expect(live.recovered).toHaveBeenCalledTimes(2);
  });
  it("coalesces duplicate events and resumes the accepted cursor after reconnect", async () => {
    const live = mount();
    await vi.advanceTimersByTimeAsync(0);
    live.next(page("1"));
    live.next(page("1"));
    await vi.advanceTimersByTimeAsync(100);
    expect(live.invalidate).toHaveBeenCalledTimes(1);
    live.reconnecting();
    live.reconnected();
    expect(live.connection.stream).toHaveBeenLastCalledWith(
      "Watch",
      board,
      "1",
    );
    live.stop();
  });
  it("lets the SDK own transport reconnect after it cancels stream callbacks", async () => {
    const live = mount();
    await vi.advanceTimersByTimeAsync(0);
    live.next(page("1"));
    live.error();
    live.reconnecting();
    await vi.advanceTimersByTimeAsync(100);
    expect(live.connection.stop).not.toHaveBeenCalled();
    expect(live.connection.start).toHaveBeenCalledTimes(1);
    live.reconnected();
    expect(live.connection.stream).toHaveBeenLastCalledWith(
      "Watch",
      board,
      "1",
    );
    await vi.advanceTimersByTimeAsync(5000);
    expect(live.connection.start).toHaveBeenCalledTimes(1);
    live.stop();
  });
  it("retries failed initial starts with backoff and polls degraded snapshots", async () => {
    const fake = fakeConnection();
    fake.connection.start.mockRejectedValue(
      new Error("sensitive provider response"),
    );
    const live = mount(fake);
    await vi.advanceTimersByTimeAsync(0);
    expect(live.status).toHaveBeenLastCalledWith("polling");
    await vi.advanceTimersByTimeAsync(999);
    expect(fake.connection.start).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(1);
    expect(fake.connection.start).toHaveBeenCalledTimes(2);
    await vi.advanceTimersByTimeAsync(2000);
    expect(fake.connection.start).toHaveBeenCalledTimes(3);
    await vi.advanceTimersByTimeAsync(7000);
    expect(live.invalidate.mock.calls.length).toBeGreaterThan(1);
    expect(JSON.stringify(live.status.mock.calls)).not.toContain("sensitive");
    live.stop();
  });
  it("recovers malformed pages without skipping their unaccepted sequence", async () => {
    const live = mount();
    await vi.advanceTimersByTimeAsync(0);
    live.next(page("2"));
    await vi.advanceTimersByTimeAsync(1000);
    expect(live.connection.stop).toHaveBeenCalled();
    expect(live.connection.stream).toHaveBeenLastCalledWith(
      "Watch",
      board,
      "0",
    );
    live.stop();
  });
  it("polls pending changes and does not storm refreshes on repeated history resets", async () => {
    const live = mount();
    await vi.advanceTimersByTimeAsync(0);
    live.next({ ...page("0", []), resetRequired: true });
    await vi.advanceTimersByTimeAsync(100);
    live.next({ ...page("0", []), resetRequired: true });
    await vi.advanceTimersByTimeAsync(100);
    expect(live.invalidate).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(10_000);
    expect(live.invalidate).toHaveBeenCalledTimes(2);
    live.stop();
  });
  it("disposes scope, timers and stale callbacks without late UI updates", async () => {
    const live = mount();
    await vi.advanceTimersByTimeAsync(0);
    live.next(page("1"));
    live.stop();
    const calls = live.status.mock.calls.length;
    live.next(page("2"));
    live.close();
    live.reconnected();
    await vi.advanceTimersByTimeAsync(60_000);
    expect(live.invalidate).not.toHaveBeenCalled();
    expect(live.status).toHaveBeenCalledTimes(calls);
    expect(live.subscription.dispose).toHaveBeenCalled();
  });
});
