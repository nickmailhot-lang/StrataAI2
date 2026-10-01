import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
} from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router-dom";
import { BoardScreen } from "./BoardScreen";
import type { BoardSnapshot } from "../../api/workManagement";
import { watchBoard } from "../../api/boardLive";
vi.mock("../../api/boardLive", () => ({ watchBoard: vi.fn(() => () => {}) }));

const fixture: BoardSnapshot = {
  board: {
    id: "board-1",
    organizationId: "org-1",
    name: "Persisted board",
    description: "Real description",
    lifecycleState: "active",
  },
  lists: [
    {
      list: {
        id: "list-1",
        name: "Planning",
        rank: "a",
        lifecycleState: "active",
      },
      cards: [
        {
          id: "card-1",
          title: "Inspect roof",
          description: "Original description",
          rank: "a",
          version: 3,
        },
      ],
    },
  ],
  access: { canView: true, canEdit: true, canMove: true, canAdminister: true },
};
function mount(path = "/app/org-1/boards/board-1") {
  const router = createMemoryRouter(
    [
      {
        path: "/app/:organizationId/boards/:boardId",
        element: <BoardScreen />,
      },
      {
        path: "/app/:organizationId/boards/:boardId/cards/:cardId",
        element: <BoardScreen />,
      },
    ],
    { initialEntries: [path] },
  );
  render(<RouterProvider router={router} />);
  return router;
}
function response(data: unknown, status = 200) {
  return new Response(JSON.stringify(data), { status });
}
afterEach(() => vi.unstubAllGlobals());

describe("PRD-01/04/07/08/09 persisted board flows", () => {
  it("updates the card face on acknowledgement and ignores an older refresh without resurrecting a removed card", async () => {
    let finishRead!: (value: Response) => void;
    const savedCard = {
      ...fixture.lists[0].cards[0],
      title: "Acknowledged title",
      version: 4,
    };
    const fetcher = vi
      .fn()
      .mockResolvedValueOnce(response(fixture))
      .mockResolvedValueOnce(response(savedCard))
      .mockImplementationOnce(
        () =>
          new Promise<Response>((resolve) => {
            finishRead = resolve;
          }),
      )
      .mockResolvedValueOnce(
        response({ ...fixture, lists: [{ ...fixture.lists[0], cards: [] }] }),
      );
    vi.stubGlobal("fetch", fetcher);
    mount("/app/org-1/boards/board-1/cards/card-1");
    fireEvent.change(await screen.findByLabelText(/Card title/), {
      target: { value: "Acknowledged title" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Save card" }));
    await waitFor(() => expect(fetcher).toHaveBeenCalledTimes(3));
    expect(
      screen.getByRole("link", { name: "Acknowledged title", hidden: true }),
    ).toBeInTheDocument();
    await act(async () => finishRead(response(fixture)));
    expect(
      screen.getByRole("link", { name: "Acknowledged title", hidden: true }),
    ).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Refresh card" }));
    await waitFor(() =>
      expect(screen.queryByLabelText(/Card title/)).not.toBeInTheDocument(),
    );
    expect(
      screen.queryByRole("link", { name: "Acknowledged title", hidden: true }),
    ).not.toBeInTheDocument();
  });
  it("preserves a dirty draft through a hung live read and retries after its deadline", async () => {
    let invalidate = () => {};
    vi.mocked(watchBoard).mockImplementationOnce((options) => {
      invalidate = options.invalidate;
      return () => {};
    });
    const newer = structuredClone(fixture);
    newer.lists[0].cards[0].version = 4;
    const fetcher = vi
      .fn()
      .mockResolvedValueOnce(response(fixture))
      .mockImplementationOnce(() => new Promise(() => {}))
      .mockResolvedValueOnce(response(newer));
    vi.stubGlobal("fetch", fetcher);
    mount("/app/org-1/boards/board-1/cards/card-1");
    const title = await screen.findByLabelText(/Card title/);
    fireEvent.change(title, { target: { value: "Keep this draft" } });
    vi.useFakeTimers();
    try {
      await act(async () => invalidate());
      await act(async () => {
        await vi.advanceTimersByTimeAsync(15_000);
      });
      expect(title).toHaveValue("Keep this draft");
      await act(async () => {
        await vi.advanceTimersByTimeAsync(10_000);
      });
    } finally {
      vi.useRealTimers();
    }
    expect(fetcher).toHaveBeenCalledTimes(3);
    expect(title).toHaveValue("Keep this draft");
    expect(screen.getByRole("button", { name: "Save card" })).toBeDisabled();
  });
  it("retries a failed live snapshot without requiring another event", async () => {
    let invalidate = () => {};
    vi.mocked(watchBoard).mockImplementationOnce((options) => {
      invalidate = options.invalidate;
      return () => {};
    });
    const newer = structuredClone(fixture);
    newer.lists[0].cards[0].title = "Automatically recovered title";
    const fetcher = vi
      .fn()
      .mockResolvedValueOnce(response(fixture))
      .mockResolvedValueOnce(response({}, 503))
      .mockResolvedValueOnce(response(newer));
    vi.stubGlobal("fetch", fetcher);
    mount();
    await screen.findByRole("link", { name: "Inspect roof" });
    vi.useFakeTimers();
    try {
      await act(async () => invalidate());
      // The initial read's promise finalizer can still be queued when the
      // rendered card appears. Flush that queued refresh and the 503 response
      // before asserting request count or advancing the retry deadline.
      await act(async () => {
        await vi.advanceTimersByTimeAsync(0);
      });
      expect(fetcher).toHaveBeenCalledTimes(2);
      await act(async () => {
        await vi.advanceTimersByTimeAsync(10_000);
      });
    } finally {
      vi.useRealTimers();
    }
    expect(fetcher).toHaveBeenCalledTimes(3);
    expect(
      await screen.findByRole("link", {
        name: "Automatically recovered title",
      }),
    ).toBeVisible();
  });
  it("preserves dirty fields and focus on a live update, then clears protected state on revoked access", async () => {
    let invalidate = () => {};
    const dispose = vi.fn();
    vi.mocked(watchBoard).mockImplementationOnce((options) => {
      invalidate = options.invalidate;
      return dispose;
    });
    const newer = structuredClone(fixture);
    newer.lists[0].cards[0] = {
      ...newer.lists[0].cards[0],
      title: "Other client edit",
      version: 4,
    };
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValueOnce(response(fixture))
        .mockResolvedValueOnce(response(newer))
        .mockResolvedValueOnce(response({}, 403)),
    );
    mount("/app/org-1/boards/board-1/cards/card-1");
    const title = await screen.findByRole("textbox", { name: /Card title/ });
    title.focus();
    fireEvent.change(title, { target: { value: "My unsaved draft" } });
    act(() => invalidate());
    await screen.findByText(/This card changed elsewhere/);
    expect(title).toHaveValue("My unsaved draft");
    expect(title).toHaveFocus();
    expect(screen.getByRole("button", { name: "Save card" })).toBeDisabled();
    act(() => invalidate());
    await waitFor(() =>
      expect(
        screen.queryByRole("textbox", { name: /Card title/ }),
      ).not.toBeInTheDocument(),
    );
    expect(screen.queryByText("Persisted board")).not.toBeInTheDocument();
    // React commits the denied view before the passive live-connection cleanup.
    await waitFor(() => expect(dispose).toHaveBeenCalledTimes(1));
  });
  it("loads authoritative data and hides write actions for read-only access", async () => {
    const fetcher = vi
      .fn()
      .mockResolvedValue(
        response({ ...fixture, access: { ...fixture.access, canEdit: false } }),
      );
    vi.stubGlobal("fetch", fetcher);
    mount();
    expect(
      await screen.findByRole("heading", { name: "Persisted board" }),
    ).toBeVisible();
    expect(screen.getByRole("link", { name: "Inspect roof" })).toHaveAttribute(
      "href",
      "/app/org-1/boards/board-1/cards/card-1",
    );
    expect(
      screen.queryByRole("button", { name: "Add list" }),
    ).not.toBeInTheDocument();
    expect(screen.queryByText("Council Operations")).not.toBeInTheDocument();
    expect(fetcher.mock.calls[0][1].credentials).toBe("include");
  });
  it("does not display a board returned for a different organization", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(response(fixture)));
    mount("/app/wrong-org/boards/board-1");
    expect(await screen.findByRole("alert")).toHaveTextContent("unavailable");
    expect(screen.queryByText("Persisted board")).not.toBeInTheDocument();
  });
  it("creates a list and refreshes from the server only after acknowledgment", async () => {
    const fetcher = vi
      .fn()
      .mockResolvedValueOnce(response(fixture))
      .mockResolvedValueOnce(response({ id: "list-2" }, 201))
      .mockResolvedValueOnce(
        response({
          ...fixture,
          lists: [
            ...fixture.lists,
            {
              list: {
                id: "list-2",
                name: "Completed",
                rank: "b",
                lifecycleState: "active",
              },
              cards: [],
            },
          ],
        }),
      );
    vi.stubGlobal("fetch", fetcher);
    mount();
    fireEvent.click(await screen.findByRole("button", { name: "Add list" }));
    fireEvent.change(screen.getByLabelText(/List name/), {
      target: { value: "   " },
    });
    fireEvent.click(screen.getByRole("button", { name: "Create" }));
    expect(screen.getByRole("alert")).toHaveTextContent("Enter a list name.");
    expect(fetcher).toHaveBeenCalledTimes(1);
    fireEvent.change(screen.getByLabelText(/List name/), {
      target: { value: "Completed" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Create" }));
    expect(
      await screen.findByRole("heading", { name: "Completed" }),
    ).toBeVisible();
    expect(fetcher.mock.calls[1][0]).toBe("/boards/board-1/lists");
    expect(JSON.parse(fetcher.mock.calls[1][1].body)).toEqual({
      name: "Completed",
    });
    expect(fetcher.mock.calls[1][1].headers.get("X-StrataAI-Request")).toBe(
      "1",
    );
  });
  it("retains conflicting edits until explicit discard and sends the current version", async () => {
    const fetcher = vi
      .fn()
      .mockResolvedValueOnce(response(fixture))
      .mockResolvedValueOnce(response({ detail: "private SQL secret" }, 409))
      .mockResolvedValueOnce(
        response({
          ...fixture,
          lists: [
            {
              ...fixture.lists[0],
              cards: [
                {
                  ...fixture.lists[0].cards[0],
                  title: "Latest roof",
                  version: 4,
                },
              ],
            },
          ],
        }),
      );
    vi.stubGlobal("fetch", fetcher);
    mount("/app/org-1/boards/board-1/cards/card-1");
    fireEvent.change(await screen.findByLabelText(/Card title/), {
      target: { value: "My draft" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Save card" }));
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "changed elsewhere",
    );
    expect(screen.getByLabelText(/Card title/)).toHaveValue("My draft");
    expect(screen.getByRole("button", { name: "Save card" })).toBeDisabled();
    expect(screen.queryByText("private SQL secret")).not.toBeInTheDocument();
    expect(JSON.parse(fetcher.mock.calls[1][1].body)).toEqual({
      title: "My draft",
      description: "Original description",
      version: 3,
    });
    fireEvent.click(
      screen.getByRole("button", {
        name: "Discard edits and load latest card",
      }),
    );
    await waitFor(() =>
      expect(screen.getByLabelText(/Card title/)).toHaveValue("Latest roof"),
    );
  });
  it("does not carry one card conflict into another card editor", async () => {
    const snapshot = {
      ...fixture,
      lists: [
        {
          ...fixture.lists[0],
          cards: [
            ...fixture.lists[0].cards,
            {
              ...fixture.lists[0].cards[0],
              id: "card-2",
              title: "Second card",
            },
          ],
        },
      ],
    };
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockImplementation((_path: string, options?: RequestInit) =>
          Promise.resolve(
            response(
              options?.method === "PATCH" ? {} : snapshot,
              options?.method === "PATCH" ? 409 : 200,
            ),
          ),
        ),
    );
    const router = mount("/app/org-1/boards/board-1/cards/card-1");
    await screen.findByLabelText(/Card title/);
    fireEvent.click(screen.getByRole("button", { name: "Save card" }));
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "changed elsewhere",
    );
    await router.navigate("/app/org-1/boards/board-1/cards/card-2");
    await waitFor(() =>
      expect(screen.getByLabelText(/Card title/)).toHaveValue("Second card"),
    );
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Save card" })).toBeEnabled();
  });
  it("preserves a dirty editor through an unavailable refresh and a newer authoritative snapshot", async () => {
    const latest = {
      ...fixture,
      lists: [
        {
          ...fixture.lists[0],
          cards: [
            { ...fixture.lists[0].cards[0], title: "Remote title", version: 4 },
          ],
        },
      ],
    };
    const fetcher = vi
      .fn()
      .mockResolvedValueOnce(response(fixture))
      .mockResolvedValueOnce(response({ detail: "private SQL" }, 503))
      .mockResolvedValueOnce(response(latest))
      .mockResolvedValueOnce(response(latest));
    vi.stubGlobal("fetch", fetcher);
    mount("/app/org-1/boards/board-1/cards/card-1");
    fireEvent.change(await screen.findByLabelText(/Card title/), {
      target: { value: "My preserved draft" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Refresh card" }));
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Service temporarily unavailable",
    );
    expect(screen.getByLabelText(/Card title/)).toHaveValue(
      "My preserved draft",
    );
    fireEvent.click(screen.getByRole("button", { name: "Refresh card" }));
    await waitFor(() =>
      expect(screen.getByRole("alert")).toHaveTextContent(
        "Your draft is preserved",
      ),
    );
    expect(screen.getByLabelText(/Card title/)).toHaveValue(
      "My preserved draft",
    );
    expect(screen.getByRole("button", { name: "Save card" })).toBeDisabled();
    fireEvent.click(
      screen.getByRole("button", {
        name: "Discard edits and load latest card",
      }),
    );
    await waitFor(() =>
      expect(screen.getByLabelText(/Card title/)).toHaveValue("Remote title"),
    );
    expect(screen.getByRole("button", { name: "Save card" })).toBeEnabled();
  });
  it.each([401, 403, 404])(
    "clears the scoped board and its draft on a %s refresh denial",
    async (status) => {
      vi.stubGlobal(
        "fetch",
        vi
          .fn()
          .mockResolvedValueOnce(response(fixture))
          .mockResolvedValueOnce(response({}, status)),
      );
      mount("/app/org-1/boards/board-1/cards/card-1");
      fireEvent.change(await screen.findByLabelText(/Card title/), {
        target: { value: "Protected draft" },
      });
      fireEvent.click(screen.getByRole("button", { name: "Refresh card" }));
      await screen.findByRole("button", { name: "Retry" });
      expect(screen.queryByLabelText(/Card title/)).not.toBeInTheDocument();
      expect(screen.queryByText("Persisted board")).not.toBeInTheDocument();
      expect(
        screen.queryByDisplayValue("Protected draft"),
      ).not.toBeInTheDocument();
    },
  );
  it("cannot restore scoped data from an older read after a denied save", async () => {
    let finishRead!: (value: Response) => void;
    let reads = 0;
    let readSignal: AbortSignal | undefined;
    const fetcher = vi.fn((_path: string, options?: RequestInit) => {
      if (options?.method === "PATCH")
        return Promise.resolve(response({}, 403));
      if (++reads === 1) return Promise.resolve(response(fixture));
      readSignal = options?.signal as AbortSignal;
      return new Promise<Response>((resolve) => {
        finishRead = resolve;
      });
    });
    vi.stubGlobal("fetch", fetcher);
    mount("/app/org-1/boards/board-1/cards/card-1");
    await screen.findByLabelText(/Card title/);
    fireEvent.click(screen.getByRole("button", { name: "Refresh card" }));
    await waitFor(() => expect(reads).toBe(2));
    fireEvent.click(screen.getByRole("button", { name: "Save card" }));
    await screen.findByRole("button", { name: "Retry" });
    expect(readSignal?.aborted).toBe(true);
    await act(async () => {
      finishRead(response(fixture));
    });
    expect(screen.queryByLabelText(/Card title/)).not.toBeInTheDocument();
    expect(screen.queryByText("Persisted board")).not.toBeInTheDocument();
  });
  it("does not resurrect an old save confirmation after access loss and a fresh authorized load", async () => {
    const savedCard = {
      ...fixture.lists[0].cards[0],
      title: "Saved title",
      version: 4,
    };
    const latest = {
      ...fixture,
      lists: [{ ...fixture.lists[0], cards: [savedCard] }],
    };
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValueOnce(response(fixture))
        .mockResolvedValueOnce(response(savedCard))
        .mockResolvedValueOnce(response(latest))
        .mockResolvedValueOnce(response({}, 403))
        .mockResolvedValueOnce(response(latest)),
    );
    mount("/app/org-1/boards/board-1/cards/card-1");
    fireEvent.change(await screen.findByLabelText(/Card title/), {
      target: { value: "Saved title" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Save card" }));
    expect(await screen.findByRole("status")).toHaveTextContent(
      "Changes saved",
    );
    await waitFor(() =>
      expect(screen.getByLabelText(/Card title/)).toHaveValue("Saved title"),
    );
    fireEvent.click(screen.getByRole("button", { name: "Refresh card" }));
    fireEvent.click(await screen.findByRole("button", { name: "Retry" }));
    expect(await screen.findByLabelText(/Card title/)).toHaveValue(
      "Saved title",
    );
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });
  it("ignores an in-flight save acknowledgment after a read revokes access", async () => {
    let finishRead!: (value: Response) => void;
    let finishSave!: (value: Response) => void;
    const fetcher = vi
      .fn()
      .mockResolvedValueOnce(response(fixture))
      .mockImplementationOnce(
        () =>
          new Promise<Response>((resolve) => {
            finishRead = resolve;
          }),
      )
      .mockImplementationOnce(
        () =>
          new Promise<Response>((resolve) => {
            finishSave = resolve;
          }),
      );
    vi.stubGlobal("fetch", fetcher);
    mount("/app/org-1/boards/board-1/cards/card-1");
    await screen.findByLabelText(/Card title/);
    fireEvent.click(screen.getByRole("button", { name: "Refresh card" }));
    await waitFor(() => expect(fetcher).toHaveBeenCalledTimes(2));
    fireEvent.click(screen.getByRole("button", { name: "Save card" }));
    await waitFor(() => expect(fetcher).toHaveBeenCalledTimes(3));
    await act(async () => {
      finishRead(response({}, 403));
    });
    await screen.findByRole("button", { name: "Retry" });
    await act(async () => {
      finishSave(
        response({
          ...fixture.lists[0].cards[0],
          title: "Old acknowledgment",
          version: 4,
        }),
      );
    });
    expect(fetcher).toHaveBeenCalledTimes(3);
    expect(screen.queryByLabelText(/Card title/)).not.toBeInTheDocument();
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });
  it("clears the previous board while a new organization is loading", async () => {
    const fetcher = vi
      .fn()
      .mockResolvedValueOnce(response(fixture))
      .mockImplementationOnce(() => new Promise(() => {}));
    vi.stubGlobal("fetch", fetcher);
    const router = mount();
    expect(await screen.findByText("Persisted board")).toBeVisible();
    await router.navigate("/app/org-2/boards/board-2");
    expect(await screen.findByLabelText("Loading board")).toBeVisible();
    expect(screen.queryByText("Persisted board")).not.toBeInTheDocument();
  });
});
