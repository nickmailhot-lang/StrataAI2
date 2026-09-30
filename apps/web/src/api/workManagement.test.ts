import { WorkMutationIntent, workRequest } from "./workManagement";

afterEach(() => vi.unstubAllGlobals());

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
