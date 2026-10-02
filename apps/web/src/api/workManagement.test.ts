import {
  WorkMutationIntent,
  WorkRequestError,
  loadBoard,
  workRequest,
} from "./workManagement";

afterEach(() => vi.unstubAllGlobals());
it('never starts a request whose scope is already cancelled', async () => {
  const fetch = vi.fn(); vi.stubGlobal('fetch', fetch); const controller = new AbortController(); controller.abort();
  await expect(workRequest('/me', { signal: controller.signal })).rejects.toMatchObject({ name: 'AbortError' });
  expect(fetch).not.toHaveBeenCalled();
});
it('rejects a late transport response after cancellation before a chained command can start', async () => {
  let finish!: (value: Response) => void; const fetch = vi.fn().mockReturnValue(new Promise<Response>(resolve => { finish = resolve; }));
  vi.stubGlobal('fetch', fetch); const controller = new AbortController();
  const command = workRequest('/me', { signal: controller.signal }).then(() => workRequest('/cards/one', { method: 'PATCH', signal: controller.signal }));
  const rejected = expect(command).rejects.toMatchObject({ name: 'AbortError' });
  controller.abort(); finish(new Response('{}')); await rejected; expect(fetch).toHaveBeenCalledTimes(1);
});
describe("PRD-22 bounded board snapshot reads", () => {
  afterEach(() => vi.useRealTimers());
  it("times out a hung fetch even when it ignores cancellation", async () => {
    vi.useFakeTimers();
    let transportSignal: AbortSignal | undefined;
    vi.stubGlobal(
      "fetch",
      vi.fn((_path, options: RequestInit) => {
        transportSignal = options.signal!;
        return new Promise(() => {});
      }),
    );
    const result = loadBoard(
      "org",
      "board",
      new AbortController().signal,
    ).catch((error: unknown) => error);
    await vi.advanceTimersByTimeAsync(14_999);
    expect(transportSignal?.aborted).toBe(false);
    await vi.advanceTimersByTimeAsync(1);
    const failure = await result;
    expect(failure).toBeInstanceOf(WorkRequestError);
    expect((failure as WorkRequestError).status).toBe(503);
    expect(transportSignal?.aborted).toBe(true);
    expect(vi.getTimerCount()).toBe(0);
  });
  it("cancels scope immediately and clears the deadline instead of reporting a timeout", async () => {
    vi.useFakeTimers();
    vi.stubGlobal(
      "fetch",
      vi.fn(() => new Promise(() => {})),
    );
    const controller = new AbortController();
    const result = loadBoard("org", "board", controller.signal).catch(
      (error: unknown) => error,
    );
    await vi.advanceTimersByTimeAsync(0);
    controller.abort();
    expect(((await result) as DOMException).name).toBe("AbortError");
    expect(vi.getTimerCount()).toBe(0);
  });
  it("does not start a read in an already cancelled scope", async () => {
    const fetcher = vi.fn();
    vi.stubGlobal("fetch", fetcher);
    const controller = new AbortController();
    controller.abort();
    await expect(
      loadBoard("org", "board", controller.signal),
    ).rejects.toMatchObject({ name: "AbortError" });
    expect(fetcher).not.toHaveBeenCalled();
  });
});

describe("PRD-07/08-TC-07 retry intent", () => {
  it.each(["network", "unavailable"])(
    "keeps the same key and body after an uncertain %s outcome",
    async (failure) => {
      const fetcher = vi.fn();
      if (failure === "network")
        fetcher.mockRejectedValueOnce(new TypeError("Network failure"));
      else fetcher.mockResolvedValueOnce(new Response("{}", { status: 503 }));
      fetcher.mockImplementation(() =>
        Promise.resolve(new Response('{"id":"one-card"}', { status: 201 })),
      );
      vi.stubGlobal("fetch", fetcher);
      const intent = new WorkMutationIntent();
      await expect(
        intent.send("/lists/one/cards", "POST", { title: "One card" }),
      ).rejects.toThrow("Keep these fields unchanged");
      await expect(
        intent.send("/lists/one/cards", "POST", { title: "One card" }),
      ).resolves.toEqual({ id: "one-card" });
      const first = fetcher.mock.calls[0][1];
      const second = fetcher.mock.calls[1][1];
      expect(first.headers.get("Idempotency-Key")).toBe(
        second.headers.get("Idempotency-Key"),
      );
      expect(first.body).toBe(second.body);
      expect(first.headers.get("X-StrataAI-Request")).toBe("1");
      await intent.send("/lists/one/cards", "POST", { title: "One card" });
      expect(fetcher.mock.calls[2][1].headers.get("Idempotency-Key")).not.toBe(
        first.headers.get("Idempotency-Key"),
      );
    },
  );

  it("starts another key when recoverable input or resource changes", async () => {
    const fetcher = vi.fn().mockRejectedValue(new TypeError("Network failure"));
    vi.stubGlobal("fetch", fetcher);
    const intent = new WorkMutationIntent();
    for (const [path, title] of [
      ["/lists/one/cards", "Original"],
      ["/lists/one/cards", "Changed"],
      ["/lists/two/cards", "Changed"],
    ])
      await expect(intent.send(path, "POST", { title })).rejects.toThrow();
    expect(
      new Set(
        fetcher.mock.calls.map((call) =>
          call[1].headers.get("Idempotency-Key"),
        ),
      ).size,
    ).toBe(3);
  });

  it.each([
    ["idempotency_key_expired", "This submission expired"],
    ["idempotency_key_reused", "This submission cannot be reused"],
    ["unknown-provider-secret", "This item changed elsewhere"],
  ])(
    "uses a fixed recovery message for code %s without exposing raw details",
    async (code, expected) => {
      vi.stubGlobal(
        "fetch",
        vi.fn().mockResolvedValue(
          new Response(
            JSON.stringify({
              code,
              title: "Sensitive SQL details",
              detail: "Provider secret body",
            }),
            { status: 409 },
          ),
        ),
      );
      try {
        await workRequest("/cards/one", { method: "PATCH" });
        throw new Error("Expected rejection");
      } catch (reason) {
        expect(reason).toBeInstanceOf(Error);
        const message = (reason as Error).message;
        expect(message).toContain(expected);
        expect(message).not.toContain("Sensitive SQL");
        expect(message).not.toContain("Provider secret");
        expect(message).not.toContain("unknown-provider-secret");
      }
    },
  );
});
