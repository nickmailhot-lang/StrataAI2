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
