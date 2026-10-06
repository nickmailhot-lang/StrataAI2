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
function navigationResponse(path: string, actor: string) {
  const query = new URL(path, 'https://fixture.test').searchParams;
  const card = query.get('cardId'), board = query.get('boardId');
  return response({ eventId: '55555555-5555-4555-8555-555555555555', actorId: actor,
    eventType: card ? 'CARD_OPENED' : 'BOARD_OPENED', entityType: card ? 'Card' : 'Board',
    organizationId: query.get('organizationId'), boardId: board, entityId: card ?? board,
    version: Number(query.get('version')), metadata: {}, createdAt: '2026-10-05T12:00:00Z' });
}
beforeEach(() => sessionStorage.clear());
afterEach(() => vi.unstubAllGlobals());

describe("PRD-01/04/07/08/09 persisted board flows", () => {
  it('keeps mutation admission busy across a queued refresh until the final read withdraws edit access', async () => {
    let invalidate = () => {};
    vi.mocked(watchBoard).mockImplementationOnce(options => { invalidate = options.invalidate; return () => {}; });
    const waiting: ((value: Response) => void)[] = [];
    let reads = 0;
    vi.stubGlobal('fetch', vi.fn(async () => {
      if (++reads === 1) return response(fixture);
      return new Promise<Response>(resolve => waiting.push(resolve));
    }));
    mount(); const workspace = await screen.findByRole('region', { name: 'Board workspace' });
    await waitFor(() => expect(workspace).toHaveAttribute('aria-busy', 'false'));
    const add = screen.getByRole('button', { name: 'Add list' });
    const enabledTransitions: boolean[] = [];
    const observer = new MutationObserver(() => enabledTransitions.push(!add.hasAttribute('disabled')));
    observer.observe(add, { attributes: true, attributeFilter: ['disabled'] });
    try {
      act(() => invalidate()); await waitFor(() => expect(waiting).toHaveLength(1));
      expect(add).toBeDisabled(); expect(workspace).toHaveAttribute('aria-busy', 'true');
      act(() => invalidate());
      await act(async () => waiting[0](response(fixture)));
      await waitFor(() => expect(waiting).toHaveLength(2));
      expect(add).toBeDisabled(); expect(workspace).toHaveAttribute('aria-busy', 'true');
      expect(enabledTransitions).not.toContain(true);
      await act(async () => waiting[1](response({ ...fixture, access: { canView: true, canEdit: false, canMove: false, canAdminister: false } })));
      await waitFor(() => expect(workspace).toHaveAttribute('aria-busy', 'false'));
      expect(screen.queryByRole('button', { name: 'Add list' })).not.toBeInTheDocument();
      expect(enabledTransitions).not.toContain(true);
    } finally { observer.disconnect(); }
  });

  it('fences competing Board commands while recovering an original metadata save after newer canonical data', async () => {
    let current: BoardSnapshot = { ...structuredClone(fixture), board: { ...fixture.board, version: 2, backgroundType: 'COLOR', backgroundValue: 'blue' } };
    const attempts: RequestInit[] = [];
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, request?: RequestInit) => {
      if (String(input) === '/boards/board-1' && request?.method === 'PATCH') {
        attempts.push(request); current = { ...current, board: { ...current.board, version: 3, name: 'Updated Board' } };
        return attempts.length === 1 ? response({ detail: 'Private failure' }, 503) : response(current.board);
      }
      return response(current);
    }));
    mount(); const edit = await screen.findByRole('button', { name: 'Edit Board details' });
    await waitFor(() => expect(edit).toBeEnabled()); fireEvent.click(edit);
    fireEvent.change(screen.getByRole('textbox', { name: 'Board name' }), { target: { value: 'Updated Board' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save Board details' }));
    const retry = await screen.findByRole('button', { name: 'Retry this Board save' }); await waitFor(() => expect(retry).toBeEnabled());
    expect(screen.getByRole('button', { name: 'Archive Board', hidden: true })).toBeDisabled();
    const addList = screen.getByRole('button', { name: 'Add list', hidden: true });
    expect(addList).toBeDisabled();
    fireEvent.click(retry); await screen.findByText('Board changes acknowledged. Current Board state is being checked.');
    await waitFor(() => { expect(addList).toBeInTheDocument(); expect(addList).toBeEnabled(); });
    expect(attempts).toHaveLength(2); expect(attempts[0].body).toBe(attempts[1].body);
    expect(new Headers(attempts[0].headers).get('Idempotency-Key')).toBe(new Headers(attempts[1].headers).get('Idempotency-Key'));
    await waitFor(() => expect(edit).toHaveFocus());
  });

  it('recovers a Board archive after canonical read-only state without releasing competing commands', async () => {
    let current: BoardSnapshot = { ...structuredClone(fixture), board: { ...fixture.board, version: 2 } };
    const attempts: RequestInit[] = [];
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, request?: RequestInit) => {
      if (String(input) === '/boards/board-1/archive') {
        attempts.push(request!); current = { ...current, board: { ...current.board, lifecycleState: 'archived', version: 3 },
          access: { ...current.access, canEdit: false, canMove: false } };
        return attempts.length === 1 ? response({ detail: 'Private failure' }, 503) : response(current.board);
      }
      return response(current);
    }));
    mount(); const archive = await screen.findByRole('button', { name: 'Archive Board' });
    await waitFor(() => expect(archive).toBeEnabled()); fireEvent.click(archive);
    fireEvent.click(screen.getByRole('button', { name: 'Confirm archive' }));
    const retry = await screen.findByRole('button', { name: 'Retry this archive' });
    await waitFor(() => expect(retry).toBeEnabled());
    expect(screen.getByText('This board is archived. Editing is unavailable.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Add list' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Refresh board', hidden: true })).toBeDisabled();
    fireEvent.click(retry); await screen.findByText('Board archive acknowledged. Current Board state is being checked.');
    await waitFor(() => expect(screen.getByRole('button', { name: 'Refresh board' })).toBeEnabled());
    expect(attempts).toHaveLength(2); expect(attempts[0].body).toBe(attempts[1].body);
    expect(new Headers(attempts[0].headers).get('Idempotency-Key')).toBe(new Headers(attempts[1].headers).get('Idempotency-Key'));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Refresh board' })).toHaveFocus());
  });

  it('fences competing mutations during unresolved attachment lifecycle and recovers its original request after a newer Card snapshot', async () => {
    const uuid = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
    const org = uuid(1), board = uuid(2), card = uuid(3), actor = uuid(8); const file = uuid(4);
    let current = structuredClone(fixture); current.board.id = board; current.board.organizationId = org;
    current.lists[0].list.id = uuid(6); current.lists[0].cards[0].id = card;
    const profile = { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-US', timezone: 'UTC' };
    const metadata = { id: file, organizationId: org, cardId: card, uploaderId: actor, kind: 1, displayName: 'Reference',
      url: 'https://example.test/', mimeType: null, sizeBytes: null, scanStatus: 0, scannedAt: null,
      createdAt: '2026-10-03T08:00:00.123456Z', updatedAt: '2026-10-03T08:00:00.123456Z', version: 1,
      deletedAt: null, lifecycleState: 0, archivedAt: null, deletedBy: null };
    const attempts: RequestInit[] = [];
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, request?: RequestInit) => {
      const path = String(input); if (path === '/me') return response(profile);
      if (path.startsWith('/navigation/observations?')) return navigationResponse(path, actor);
      if (path === `/cards/${card}/attachments`) return response({ organizationId: org, boardId: board, cardId: card,
        cardVersion: current.lists[0].cards[0].version, items: [metadata], canEdit: true, nextCursor: null });
      if (path === `/cards/${card}/attachments/${file}/archive`) {
        attempts.push(request!); current = structuredClone(current); current.lists[0].cards[0].version = 4;
        return attempts.length === 1 ? response({ code: 'work_storage_unavailable' }, 503)
          : response({ organizationId: org, boardId: board, cardId: card, cardVersion: 4, changed: true,
            attachment: { ...metadata, lifecycleState: 1, version: 2, updatedAt: '2026-10-03T08:01:00.123456Z', archivedAt: '2026-10-03T08:01:00.123456Z' } });
      }
      return response(current);
    }));
    mount(`/app/${org}/boards/${board}/cards/${card}`);
    const manage = await screen.findByRole('button', { name: 'Manage attachments' }); await waitFor(() => expect(manage).toBeEnabled()); fireEvent.click(manage);
    fireEvent.click(await screen.findByRole('button', { name: 'Archive attachment Reference' }));
    const confirmArchive = screen.getByRole('button', { name: 'Confirm attachment archive' });
    await waitFor(() => expect(confirmArchive).toBeEnabled()); fireEvent.click(confirmArchive);
    await waitFor(() => expect(attempts).toHaveLength(1));
    const retry = await screen.findByRole('button', { name: 'Retry original attachment change' }); await waitFor(() => expect(retry).toBeEnabled());
    for (const name of ['Add link attachment', 'Add file attachment', 'Add checklist', 'Save card', 'Close'])
      expect(screen.getByRole('button', { name })).toBeDisabled();
    expect(attempts).toHaveLength(1); fireEvent.click(retry); await screen.findByText('Attachment archived.');
    await waitFor(() => expect(screen.getByRole('button', { name: 'Close' })).toBeEnabled());
    expect(attempts).toHaveLength(2); expect(attempts[1].body).toBe(attempts[0].body);
    expect(JSON.parse(attempts[0].body as string)).toEqual({ cardVersion: 3, version: 1 });
    expect(new Headers(attempts[1].headers).get('Idempotency-Key')).toBe(new Headers(attempts[0].headers).get('Idempotency-Key'));
    // Covers admission, archive refusal, snapshot refresh and exact retry;
    // allow the same multi-step fixture budget as upload recovery below.
  }, 10_000);
  it('locks competing Card mutations during unconfirmed file upload and recovers the same original request after a newer snapshot', async () => {
    const org = '11111111-1111-4111-8111-111111111111'; const board = '22222222-2222-4222-8222-222222222222';
    const card = '33333333-3333-4333-8333-333333333333'; const actor = '44444444-4444-4444-8444-444444444444';
    const current = structuredClone(fixture); current.board.id = board; current.board.organizationId = org; current.lists[0].cards[0].id = card;
    const profile = { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-US', timezone: 'UTC' };
    const attempts: RequestInit[] = [];
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, request?: RequestInit) => {
      const path = String(input);
      if (path.startsWith('/navigation/observations?')) return navigationResponse(path, actor);
      if (path === '/me') return response(profile);
      if (path.endsWith('/attachment-upload-options')) return response({ organizationId: org, boardId: board, cardId: card,
        cardVersion: 3, maximumBytes: 20971520, allowedMimeTypes: ['application/pdf'] });
      if (path.endsWith('/attachments') && request?.method === 'POST') {
        attempts.push(request); current.lists[0].cards[0].version = 4;
        if (attempts.length === 1) return response({ code: 'work_storage_unavailable', detail: 'private provider text' }, 503);
        return response({ organizationId: org, boardId: board, cardId: card, cardVersion: 4, attachment: {
          id: '55555555-5555-4555-8555-555555555555', organizationId: org, cardId: card, uploaderId: actor, kind: 0,
          displayName: 'Document.pdf', mimeType: 'application/pdf', sizeBytes: 9, url: null, scanStatus: 1, scannedAt: null,
          createdAt: '2026-10-03T08:00:00.123456Z', updatedAt: '2026-10-03T08:00:00.123456Z', version: 1, deletedAt: null,
          lifecycleState: 0, archivedAt: null, deletedBy: null } });
      }
      return response(current);
    }));
    mount(`/app/${org}/boards/${board}/cards/${card}`);
    const add = await screen.findByRole('button', { name: 'Add file attachment' });
    await waitFor(() => expect(add).toBeEnabled()); fireEvent.click(add);
    const selection = await screen.findByLabelText('File to attach'); await waitFor(() => expect(selection).toBeEnabled());
    const file = new File(['%PDF-1.7\n'], 'Document.pdf', { type: 'text/html' });
    Object.defineProperty(file, 'slice', { value: (start: number, end: number) => ({ arrayBuffer: async () => new TextEncoder().encode('%PDF-1.7\n').slice(start, end).buffer }) });
    fireEvent.change(selection, { target: { files: [file] } });
    const upload = screen.getByRole('button', { name: 'Upload selected file' });
    await waitFor(() => expect(upload).toBeEnabled()); fireEvent.click(upload);
    await waitFor(() => expect(attempts).toHaveLength(1));
    const retry = await screen.findByRole('button', { name: 'Retry original file upload' }); await waitFor(() => expect(retry).toBeEnabled());
    expect(screen.getByRole('button', { name: 'Add link attachment' })).toBeDisabled(); expect(screen.getByRole('button', { name: 'Close' })).toBeDisabled();
    expect(attempts).toHaveLength(1); expect(attempts[0].body).toBe(file);
    fireEvent.click(retry); await screen.findByText('File attached. Safety scan pending.');
    expect(attempts).toHaveLength(2); expect(attempts[1].body).toBe(file);
    expect(new Headers(attempts[1].headers).get('X-Card-Version')).toBe('3');
    expect(new Headers(attempts[1].headers).get('Idempotency-Key')).toBe(new Headers(attempts[0].headers).get('Idempotency-Key'));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Close' })).toBeEnabled());
  }, 10_000);
  it('updates canvas due descriptions on live completion and removes them when dates clear', async () => {
    let invalidate = () => {};
    vi.mocked(watchBoard).mockImplementationOnce(options => { invalidate = options.invalidate; return () => {}; });
    const dated = structuredClone(fixture);
    Object.assign(dated.lists[0].cards[0], { startAt: null, dueAt: '2040-01-03T08:00:00Z', dueTimezone: 'UTC', dueHasTime: true, dueComplete: false });
    const profile = { id: '22222222-2222-2222-2222-222222222222', version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-US', timezone: 'UTC' };
    let current = dated;
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => response(String(input).endsWith('/me') ? profile : current)));
    mount();
    await waitFor(() => expect(screen.getByRole('link', { name: 'Inspect roof' })).toHaveAccessibleDescription('Upcoming'));
    current = structuredClone(dated); Object.assign(current.lists[0].cards[0], { dueComplete: true, version: 4 });
    act(() => invalidate());
    await waitFor(() => expect(screen.getByRole('link', { name: 'Inspect roof' })).toHaveAccessibleDescription('Complete'));
    current = structuredClone(dated); Object.assign(current.lists[0].cards[0], { dueAt: null, dueTimezone: null, dueHasTime: false, dueComplete: false, version: 5 });
    act(() => invalidate());
    await waitFor(() => expect(screen.getByRole('link', { name: 'Inspect roof' })).not.toHaveAttribute('aria-describedby'));
    expect(screen.queryByText('Complete')).not.toBeInTheDocument();
  });
  it("hides assignee names while refreshing and clears them on revoked Board access", async () => {
    let invalidate = () => {}; let finish!: (value: Response) => void;
    vi.mocked(watchBoard).mockImplementationOnce(options => { invalidate = options.invalidate; return () => {}; });
    const named = { ...fixture, cardMembers: { 'card-1': { cardVersion: 3, total: 1, items: [{ userId: '44444444-4444-4444-4444-444444444444', displayName: 'Protected member' }] } } };
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(response(named)).mockImplementationOnce(() => new Promise<Response>(resolve => { finish = resolve; })));
    mount(); await screen.findByRole('img', { name: 'Assigned to Protected member' });
    expect(screen.getByRole('link', { name: 'Inspect roof' })).toBeVisible();
    act(() => invalidate()); await waitFor(() => expect(finish).toBeDefined());
    expect(screen.queryByRole('img', { name: 'Assigned to Protected member' })).not.toBeInTheDocument();
    await act(async () => finish(response({}, 403))); await screen.findByRole('button', { name: 'Retry' });
    expect(screen.queryByRole('group', { name: 'Card assignee indicators' })).not.toBeInTheDocument();
  });
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
    let invalidate: (() => void) | undefined;
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
    await waitFor(() => expect(invalidate).toBeDefined());
    vi.useFakeTimers();
    try {
      await act(async () => invalidate!());
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
  it.each(['permission', 'board', 'list'])('omits movement when current %s admission is unavailable', async reason => {
    const value = { ...fixture, board: { ...fixture.board, lifecycleState: reason === 'board' ? 'archived' : 'active' },
      access: { ...fixture.access, canMove: reason !== 'permission' },
      lists: fixture.lists.map(column => ({ ...column, list: { ...column.list, lifecycleState: reason === 'list' ? 'archived' : 'active' } })) };
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response(value)));
    mount('/app/org-1/boards/board-1/cards/card-1');
    await screen.findByRole('heading', { name: 'Card details' });
    await screen.findByText('Inspect roof');
    expect(screen.queryByRole('button', { name: 'Move card' })).not.toBeInTheDocument();
  });
  it('reloads canonical placement after a bound append acknowledgment', async () => {
    const destination = { list: { id: 'list-2', name: 'Complete', rank: 'b', lifecycleState: 'active' }, cards: [] };
    const initial = { ...fixture, lists: [...fixture.lists, destination] };
    const moved = { ...fixture.lists[0].cards[0], rank: '500000000000000000000000000000', version: 4,
      organizationId: 'org-1', boardId: 'board-1', listId: 'list-2' };
    const latest = { ...fixture, lists: [{ ...fixture.lists[0], cards: [] }, { ...destination, cards: [moved] }] };
    let completeMove: ((value: Response) => void) | undefined;
    const fetcher = vi.fn().mockResolvedValueOnce(response(initial))
      .mockImplementationOnce(() => new Promise<Response>(resolve => { completeMove = resolve; }))
      .mockResolvedValueOnce(response(latest));
    vi.stubGlobal('fetch', fetcher); mount('/app/org-1/boards/board-1/cards/card-1');
    fireEvent.click(await screen.findByRole('button', { name: 'Move card' }));
    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Destination list' }));
    fireEvent.click(await screen.findByRole('option', { name: 'Complete' }));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm card move' }));
    expect(screen.getByText('Saving move. Placement is provisional until confirmed.')).toHaveAttribute('role', 'status');
    expect(screen.getByText('Complete', { selector: 'h3' }).closest('section')).toHaveTextContent('Inspect roof');
    expect(screen.getByRole('button', { name: 'Close' })).toBeDisabled();
    await act(async () => { completeMove?.(response(moved)); });
    await screen.findByText('Move acknowledged. Current placement is being checked.');
    await waitFor(() => expect(fetcher).toHaveBeenCalledTimes(3));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Move card' })).toBeEnabled());
    expect(fetcher.mock.calls[1][0]).toBe('/cards/card-1/move');
    expect(JSON.parse(fetcher.mock.calls[1][1].body)).toEqual({ destinationListId: 'list-2', expectedVersion: 3 });
    expect(fetcher.mock.calls[2][0]).toBe('/boards/board-1');
    fireEvent.click(screen.getByRole('button', { name: 'Close' }));
    await waitFor(() => expect(screen.getByRole('link', { name: 'Inspect roof' })).toHaveFocus());
  });
  it('shows provisional list order before persistence and restores canonical order after an uncertain result', async () => {
    const initial = { ...fixture, lists: [{ ...fixture.lists[0], list: { ...fixture.lists[0].list, version: 1 } },
      { list: { id: 'list-2', name: 'Complete', rank: 'b', lifecycleState: 'active', version: 1 }, cards: [] }] };
    let failMove: ((reason: Error) => void) | undefined;
    const fetcher = vi.fn().mockResolvedValueOnce(response(initial))
      .mockImplementationOnce(() => new Promise<Response>((_, reject) => { failMove = reject; }))
      .mockResolvedValueOnce(response(initial));
    vi.stubGlobal('fetch', fetcher); mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Move Complete list' }));
    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Position for Complete' }));
    fireEvent.click(await screen.findByRole('option', { name: 'Before Planning' }));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm list move' }));
    expect(screen.getAllByRole('heading', { level: 3 }).map(heading => heading.textContent)).toEqual(['Complete', 'Planning']);
    expect(screen.getByText('Saving list position. Ordering is provisional until confirmed.')).toHaveAttribute('role', 'status');
    await act(async () => { failMove?.(new Error('Lost response')); });
    await screen.findByRole('button', { name: 'Retry this list move' });
    await waitFor(() => expect(fetcher).toHaveBeenCalledTimes(3));
    expect(screen.getAllByRole('heading', { level: 3 }).map(heading => heading.textContent)).toEqual(['Planning', 'Complete']);
    expect(screen.getByRole('link', { name: 'Inspect roof' })).toBeVisible();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Drag Complete list' })).toBeDisabled());
    expect(fetcher.mock.calls.filter(call => call[1]?.method === 'PATCH')).toHaveLength(1);
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
  it('keeps Card archive recovery outside the removed canonical editor and returns to the Board', async () => {
    const active = { ...fixture, lists: [{ ...fixture.lists[0], list: { ...fixture.lists[0].list, version: 1 } }] };
    let archived = false; const writes: RequestInit[] = [];
    vi.stubGlobal('fetch', vi.fn((_path: string, init?: RequestInit) => {
      if (_path === '/me') return Promise.resolve(response({ id: '00000000-0000-4000-8000-000000000001',
        version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-US', timezone: 'UTC' }));
      if (init?.method === 'POST') {
        writes.push(init); archived = true;
        return writes.length === 1 ? Promise.reject(new Error('Lost')) : Promise.resolve(response({ ...fixture.lists[0].cards[0],
          organizationId: 'org-1', boardId: 'board-1', listId: 'list-1', version: 4, lifecycleState: 'archived' }));
      }
      return Promise.resolve(response(archived ? { ...active, lists: [{ ...active.lists[0], cards: [] }] } : active));
    }));
    const router = mount('/app/org-1/boards/board-1/cards/card-1');
    fireEvent.click(await screen.findByRole('button', { name: 'Archive Card' }));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm Card archive' }));
    const retry = await screen.findByRole('button', { name: 'Retry this Card archive' });
    await waitFor(() => expect(retry).toBeEnabled()); expect(screen.getByRole('button', { name: 'Close' })).toBeDisabled();
    expect(screen.getByText('This card is unavailable in this board.')).toBeInTheDocument();
    fireEvent.click(retry); await waitFor(() => expect(router.state.location.pathname).toBe('/app/org-1/boards/board-1'));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument(), { timeout: 5_000 });
    await waitFor(() => expect(screen.getByRole('button', { name: 'Refresh board' })).toHaveFocus());
    expect(writes).toHaveLength(2); expect(writes[1].body).toBe(writes[0].body);
    expect(new Headers(writes[1].headers).get('Idempotency-Key')).toBe(new Headers(writes[0].headers).get('Idempotency-Key'));
  });
  it('keeps cross-Board recovery after source removal without treating the moved Card as denied archived detail', async () => {
    const uuid = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
    const org = uuid(1), board = uuid(2), cardId = uuid(3), destination = uuid(4), list = uuid(5);
    const active = structuredClone(fixture); active.board.id = board; active.board.organizationId = org;
    active.lists[0].cards[0].id = cardId;
    const target = { ...active, board: { ...active.board, id: destination, name: 'Destination' },
      lists: [{ list: { id: list, name: 'Destination List', rank: '1', lifecycleState: 'active' }, cards: [] }] };
    const writes: RequestInit[] = []; let moved = false; let archivedReads = 0;
    vi.stubGlobal('fetch', vi.fn((path: string, init?: RequestInit) => {
      if (path.startsWith('/navigation/observations?')) return Promise.resolve(navigationResponse(path, uuid(8)));
      if (path === '/me') return Promise.resolve(response({ id: uuid(8), status: 'ACTIVE', version: 1, emailVerified: true, locale: 'en-US', timezone: 'UTC' }));
      if (path === `/organizations/${org}/boards`) return Promise.resolve(response([{ id: destination, name: 'Destination', version: 1 }]));
      if (path === `/boards/${destination}`) return Promise.resolve(response(target));
      if (path.endsWith('/archived-details')) { archivedReads++; return Promise.resolve(response({}, 404)); }
      if (path === `/cards/${cardId}/move`) {
        writes.push(init!); moved = true;
        return writes.length === 1 ? Promise.reject(new Error('Lost move response')) : Promise.resolve(response({ ...active.lists[0].cards[0],
          organizationId: org, boardId: destination, listId: list, rank: '500000000000000000000000000000', version: 4 }));
      }
      return Promise.resolve(response(moved ? { ...active, lists: [] } : active));
    }));
    mount(`/app/${org}/boards/${board}/cards/${cardId}`);
    const move = await screen.findByRole('button', { name: 'Move to another Board' });
    await waitFor(() => expect(move).toBeEnabled()); fireEvent.click(move);
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Destination Board' })).not.toHaveAttribute('aria-disabled', 'true'));
    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Destination Board' })); fireEvent.click(await screen.findByRole('option', { name: 'Destination' }));
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Destination List on another Board' })).not.toHaveAttribute('aria-disabled', 'true'));
    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Destination List on another Board' })); fireEvent.click(await screen.findByRole('option', { name: 'Destination List' }));
    const confirm = screen.getByRole('button', { name: 'Confirm move to another Board' });
    await waitFor(() => expect(confirm).toBeEnabled()); fireEvent.click(confirm);
    await waitFor(() => expect(writes).toHaveLength(1));
    const retry = await screen.findByRole('button', { name: 'Retry this cross-Board move' }); await waitFor(() => expect(retry).toBeEnabled());
    expect(screen.getByRole('button', { name: 'Close' })).toBeDisabled(); expect(archivedReads).toBe(0);
    expect(screen.getByText('Persisted board', { selector: 'h2' })).toBeVisible(); fireEvent.click(retry);
    await screen.findByText('Move acknowledged. Check the destination Board for current placement.');
    await waitFor(() => expect(screen.getByRole('button', { name: 'Close' })).toBeEnabled());
    expect(screen.getByRole('link', { name: 'Open Card on destination Board' })).toHaveAttribute('href', `/app/${org}/boards/${destination}/cards/${cardId}`);
    expect(archivedReads).toBe(0); expect(writes).toHaveLength(2); expect(writes[1].body).toBe(writes[0].body);
    expect(new Headers(writes[1].headers).get('Idempotency-Key')).toBe(new Headers(writes[0].headers).get('Idempotency-Key'));
    fireEvent.click(screen.getByRole('button', { name: 'Close' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Refresh board' })).toHaveFocus());
  }, 10_000);
  it('keeps a lost List copy bound through a canonical source revision and returns focus after receipt recovery', async () => {
    const board = '11111111-1111-1111-1111-111111111111'; const org = '22222222-2222-2222-2222-222222222222';
    const list = '33333333-3333-3333-3333-333333333333'; const copiedId = '44444444-4444-4444-4444-444444444444';
    const rank = '500000000000000000000000000000';
    const active = { ...fixture, board: { ...fixture.board, id: board, organizationId: org },
      lists: [{ ...fixture.lists[0], list: { ...fixture.lists[0].list, id: list, rank, version: 1 } }] };
    let copied = false; const writes: RequestInit[] = [];
    vi.stubGlobal('fetch', vi.fn((path: string, init?: RequestInit) => {
      if (path === `/organizations/${org}/boards`) return Promise.resolve(response([{ id: board, name: 'Persisted board', version: 1 }]));
      if (init?.method === 'POST') {
        writes.push(init); copied = true;
        return writes.length === 1 ? Promise.reject(new Error('Lost')) : Promise.resolve(response({ id: copiedId,
          organizationId: org, boardId: board, name: 'Planning copy', rank, version: 1, lifecycleState: 'active' }, 201));
      }
      return Promise.resolve(response(copied ? { ...active, lists: [{ ...active.lists[0], list: { ...active.lists[0].list, name: 'Newer source', version: 2 } }] } : active));
    }));
    mount(`/app/${org}/boards/${board}`);
    fireEvent.click(await screen.findByRole('button', { name: 'Copy list' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Reload copy destinations' })).toBeEnabled());
    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'List to copy' }));
    fireEvent.click(screen.getByRole('option', { name: 'Planning' }));
    fireEvent.click(screen.getByRole('button', { name: 'Review List copy' }));
    await screen.findByText('Copy Planning as Planning copy to Persisted board?');
    fireEvent.click(screen.getByRole('button', { name: 'Confirm List copy' }));
    const retry = await screen.findByRole('button', { name: 'Retry this List copy' });
    await waitFor(() => expect(retry).toBeEnabled());
    expect(screen.queryByRole('button', { name: 'Cancel copy' })).not.toBeInTheDocument();
    expect(screen.getByText('Newer source', { selector: 'h3' })).toBeInTheDocument();
    fireEvent.click(retry);
    await screen.findByText('List copy acknowledged. Check its destination Board for the current copy.');
    await waitFor(() => expect(screen.getByRole('button', { name: 'Refresh board' })).toHaveFocus());
    expect(writes).toHaveLength(2); expect(writes[1].body).toBe(writes[0].body);
    expect(JSON.parse(writes[0].body as string)).toEqual({ destinationBoardId: board, name: 'Planning copy', version: 1 });
    expect(new Headers(writes[1].headers).get('Idempotency-Key')).toBe(new Headers(writes[0].headers).get('Idempotency-Key'));
  });
  it('fences other Board mutations while URL attachment receipt is unresolved but permits its original retry after newer snapshot', async () => {
    const uuid = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
    const org = uuid(1), board = uuid(2), card = uuid(3), actor = uuid(8);
    let current = structuredClone(fixture);
    current.board.id = board; current.board.organizationId = org;
    current.lists[0].list.id = uuid(6); current.lists[0].cards[0].id = card;
    const profile = { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-US', timezone: 'UTC' };
    const writes: RequestInit[] = [];
    const receipt = { organizationId: org, boardId: board, cardId: card, cardVersion: 4, attachment: {
      id: uuid(4), organizationId: org, cardId: card, uploaderId: actor, kind: 1, displayName: 'Reference',
      url: 'https://example.test/reference', mimeType: null, sizeBytes: null, scanStatus: 0, scannedAt: null,
      createdAt: '2026-10-03T08:00:00.123456Z', updatedAt: '2026-10-03T08:00:00.123456Z', version: 1, deletedAt: null,
      lifecycleState: 0, archivedAt: null, deletedBy: null } };
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, options?: RequestInit) => {
      const path = String(input);
      if (path.startsWith('/navigation/observations?')) return navigationResponse(path, actor);
      if (path.endsWith('/me')) return response(profile);
      if (path.endsWith('/attachments/url')) {
        writes.push(options!); current = structuredClone(current); current.lists[0].cards[0].version = 4;
        return writes.length === 1 ? response({ code: 'work_storage_unavailable' }, 503) : response(receipt);
      }
      return response(current);
    }));
    mount(`/app/${org}/boards/${board}/cards/${card}`);
    const add = await screen.findByRole('button', { name: 'Add link attachment' }); await waitFor(() => expect(add).toBeEnabled()); fireEvent.click(add);
    fireEvent.change(await screen.findByLabelText(/New link attachment title/), { target: { value: 'Reference' } });
    fireEvent.change(screen.getByLabelText(/Attachment URL/), { target: { value: 'https://example.test/reference' } });
    const create = screen.getByRole('button', { name: 'Create link attachment' });
    await waitFor(() => expect(create).toBeEnabled()); fireEvent.click(create);
    await waitFor(() => expect(writes).toHaveLength(1));
    const retry = await screen.findByRole('button', { name: 'Retry link attachment creation' }); await waitFor(() => expect(retry).toBeEnabled());
    const addChecklist = screen.getByRole('button', { name: 'Add checklist' });
    expect(addChecklist).toBeDisabled(); expect(screen.getByRole('button', { name: 'Save card' })).toBeDisabled();
    expect(writes).toHaveLength(1); fireEvent.click(retry); await screen.findByText('Link attachment created.');
    await waitFor(() => { expect(addChecklist).toBeInTheDocument(); expect(addChecklist).toBeEnabled(); });
    expect(writes).toHaveLength(2); expect(writes[1].body).toBe(writes[0].body);
    expect(JSON.parse(writes[0].body as string)).toEqual({ title: 'Reference', url: 'https://example.test/reference', cardVersion: 3 });
    expect(new Headers(writes[1].headers).get('Idempotency-Key')).toBe(new Headers(writes[0].headers).get('Idempotency-Key'));
  });

  it('fences competing Card changes for an unresolved cover receipt and permits only its original retry after a newer snapshot', async () => {
    const uuid = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
    const org = uuid(1), board = uuid(2), card = uuid(3), file = uuid(4), actor = uuid(8);
    let current = structuredClone(fixture); current.board.id = board; current.board.organizationId = org;
    current.lists[0].list.id = uuid(6); current.lists[0].cards[0].id = card;
    const profile = { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-US', timezone: 'UTC' };
    const scope = { organizationId: org, boardId: board, cardId: card }; const writes: RequestInit[] = [];
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, options?: RequestInit) => {
      const path = String(input);
      if (path.startsWith('/navigation/observations?')) return navigationResponse(path, actor);
      if (path.endsWith('/me')) return response(profile);
      if (options?.method === 'PUT' && path.endsWith('/cover')) {
        writes.push(options); current = structuredClone(current); current.lists[0].cards[0].version = 4;
        current.lists[0].cards[0].hasCover = true;
        return writes.length === 1 ? response({ code: 'work_storage_unavailable' }, 503)
          : response({ ...scope, cardVersion: 4, attachmentId: file, attachmentVersion: 3, changed: true });
      }
      if (path.endsWith('/cover')) return response({ ...scope, cardVersion: 3, attachmentId: null, attachmentVersion: null, canEdit: true, isPublic: false });
      if (path.endsWith('/cover/candidates')) return response({ ...scope, cardVersion: 3, items: [{ attachmentId: file, attachmentVersion: 3,
        displayName: 'Site image.png', createdAt: '2026-10-03T08:00:00.123456Z' }], nextCursor: null, canEdit: true, isPublic: false });
      return response(current);
    }));
    mount(`/app/${org}/boards/${board}/cards/${card}`);
    const review = await screen.findByRole('button', { name: 'Review Card cover' }); await waitFor(() => expect(review).toBeEnabled()); fireEvent.click(review);
    fireEvent.click(await screen.findByRole('button', { name: 'Use Site image.png as cover' }));
    const confirmCover = screen.getByRole('button', { name: 'Confirm Card cover' });
    await waitFor(() => expect(confirmCover).toBeEnabled()); fireEvent.click(confirmCover);
    await waitFor(() => expect(writes).toHaveLength(1));
    const retry = await screen.findByRole('button', { name: 'Retry original cover change' }); await waitFor(() => expect(retry).toBeEnabled());
    for (const name of ['Add checklist', 'Save card', 'Add link attachment', 'Manage attachments', 'Close'])
      expect(screen.getByRole('button', { name })).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Discard cover review and load latest' })).not.toBeInTheDocument();
    fireEvent.click(retry); await screen.findByText('Card cover updated.');
    await waitFor(() => expect(screen.getByRole('button', { name: 'Add checklist' })).toBeEnabled());
    expect(writes).toHaveLength(2); expect(writes[1].body).toBe(writes[0].body);
    expect(JSON.parse(writes[0].body as string)).toEqual({ attachmentId: file, attachmentVersion: 3, cardVersion: 3, publicVisibilityConfirmed: false });
    expect(new Headers(writes[1].headers).get('Idempotency-Key')).toBe(new Headers(writes[0].headers).get('Idempotency-Key'));
  }, 10_000);

  it('fences competing changes and Card closure while recovering an original comment acknowledgment', async () => {
    const uuid = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
    const org = uuid(1), board = uuid(2), card = uuid(3), actor = uuid(8);
    let current = structuredClone(fixture); current.board.id = board; current.board.organizationId = org;
    current.lists[0].list.id = uuid(6); current.lists[0].cards[0].id = card;
    const profile = { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-US', timezone: 'UTC' };
    const scope = { organizationId: org, boardId: board, cardId: card }; const writes: RequestInit[] = [];
    const comment = { id: uuid(4), organizationId: org, cardId: card, authorId: actor, content: 'Shared comment',
      createdAt: '2026-10-03T08:00:00.123456Z', updatedAt: '2026-10-03T08:00:00.123456Z', version: 1, editedAt: null, deletedAt: null, deletedBy: null };
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, options?: RequestInit) => {
      const path = String(input); if (path.endsWith('/me')) return response(profile);
      if (path.startsWith('/navigation/observations?')) return navigationResponse(path, actor);
      if (options?.method === 'POST' && path.endsWith('/comments')) {
        writes.push(options); current = structuredClone(current); current.lists[0].cards[0].version = 4;
        return writes.length === 1 ? response({ code: 'work_storage_unavailable' }, 503) : response({ ...scope, cardVersion: 4, comment, changed: true });
      }
      if (path.endsWith('/comments')) return response({ ...scope, cardVersion: 3, items: [], nextCursor: null, canComment: true });
      return response(current);
    }));
    mount(`/app/${org}/boards/${board}/cards/${card}`);
    const review = await screen.findByRole('button', { name: 'Review Card comments' }); await waitFor(() => expect(review).toBeEnabled()); fireEvent.click(review);
    fireEvent.click(await screen.findByRole('button', { name: 'Add comment' }));
    fireEvent.change(screen.getByRole('textbox', { name: 'New comment' }), { target: { value: 'Shared comment' } });
    for (const name of ['Add checklist', 'Save card', 'Add link attachment', 'Manage attachments', 'Review Card cover', 'Close'])
      expect(screen.getByRole('button', { name })).toBeDisabled();
    const saveComment = screen.getByRole('button', { name: 'Save comment' });
    await waitFor(() => expect(saveComment).toBeEnabled()); fireEvent.click(saveComment);
    await waitFor(() => expect(writes).toHaveLength(1));
    const retry = await screen.findByRole('button', { name: 'Retry original comment change' }); await waitFor(() => expect(retry).toBeEnabled());
    for (const name of ['Add checklist', 'Save card', 'Add link attachment', 'Manage attachments', 'Review Card cover', 'Close'])
      expect(screen.getByRole('button', { name })).toBeDisabled();
    fireEvent.click(retry); await screen.findByText('Comment added.');
    await waitFor(() => expect(screen.getByRole('button', { name: 'Close' })).toBeEnabled());
    expect(writes).toHaveLength(2); expect(writes[1].body).toBe(writes[0].body);
    expect(JSON.parse(writes[0].body as string)).toEqual({ content: 'Shared comment', cardVersion: 3 });
    expect(new Headers(writes[1].headers).get('Idempotency-Key')).toBe(new Headers(writes[0].headers).get('Idempotency-Key'));
  }, 10_000);

  it('renders only admitted cover hints on the Card face/detail and retires images during live refresh/removal', async () => {
    const uuid = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
    const current = structuredClone(fixture); current.board.id = uuid(2); current.board.organizationId = uuid(1);
    current.lists[0].list.id = uuid(6); current.lists[0].cards[0].id = uuid(3); current.lists[0].cards[0].hasCover = true;
    const removed = structuredClone(current); removed.lists[0].cards[0].version = 4; removed.lists[0].cards[0].hasCover = false;
    let invalidate: (() => void) | undefined; let finish: ((value: Response) => void) | undefined;
    vi.mocked(watchBoard).mockImplementationOnce(options => { invalidate = options.invalidate; return () => {}; });
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(response(current)).mockImplementationOnce(() => new Promise<Response>(resolve => { finish = resolve; })));
    mount(`/app/${uuid(1)}/boards/${uuid(2)}/cards/${uuid(3)}`);
    await screen.findByRole('heading', { name: 'Card details' });
    // MUI hides the canvas from assistive technology while details are open.
    await screen.findByRole('img', { name: 'Card cover' });
    const images = [...document.querySelectorAll('img[alt="Card cover"]')]; expect(images).toHaveLength(2);
    expect(images.map(image => image.getAttribute('loading')).sort()).toEqual(['eager', 'lazy']);
    for (const image of images) expect(image).toHaveAttribute('src', `/cards/${uuid(3)}/cover/image?cardVersion=3`);
    await waitFor(() => expect(invalidate).toBeDefined()); act(() => invalidate!());
    await waitFor(() => expect(document.querySelectorAll('img[alt="Card cover"]')).toHaveLength(0));
    await waitFor(() => expect(finish).toBeDefined()); await act(async () => finish!(response(removed)));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Save card' })).toBeEnabled());
    expect(document.querySelectorAll('img[alt="Card cover"]')).toHaveLength(0);
  });

});
