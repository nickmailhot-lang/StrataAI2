import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router-dom";
import { OrganizationHome } from "./OrganizationHome";
const live = vi.hoisted(() => ({ watch: vi.fn<(options: { organizationId: string; userId: string; audience: string;
  invalidate(): void; reset(): void; unavailable(): void }) => () => void>(() => vi.fn()) }));
vi.mock('../kanban/organizationBoardLive', () => ({ watchOrganizationBoards: live.watch }));
const profile = { id: '22222222-2222-4222-8222-222222222222', version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'America/Vancouver' };
function stubFetch(delegate: (path: string, options?: RequestInit) => unknown) {
  vi.stubGlobal('fetch', (path: string, options?: RequestInit) => {
    if (path === '/me') return Promise.resolve(response(profile));
    if (path === '/navigation/observations?kind=context') return Promise.resolve(response({
      eventId: '33333333-3333-4333-8333-333333333333', entityId: '33333333-3333-4333-8333-333333333333',
      actorId: profile.id, eventType: 'APPLICATION_CONTEXT_CHANGED', entityType: 'ApplicationContext',
      organizationId: null, boardId: null, version: 1, metadata: {}, createdAt: '2026-10-05T12:00:00Z',
    }));
    return delegate(path, options);
  });
}

const organizations = [
  {
    organization: {
      id: "org-1",
      name: "Council",
      description: "Organization description",
      status: 0,
    },
    role: 0,
  },
];
function response(value: unknown, status = 200) {
  return new Response(JSON.stringify(value), { status });
}
function mount(path = "/app") {
  const router = createMemoryRouter(
    [
      { path: "/app", element: <OrganizationHome /> },
      { path: "/app/:organizationId", element: <OrganizationHome /> },
      {
        path: "/app/:organizationId/boards/:boardId",
        element: <h1>Created board destination</h1>,
      },
      { path: "/login", element: <h1>Sign in destination</h1> },
    ],
    { initialEntries: [path] },
  );
  render(<RouterProvider router={router} />);
  return router;
}
beforeEach(() => sessionStorage.clear());
afterEach(() => { vi.unstubAllGlobals(); vi.clearAllMocks(); });
describe("PRD-01/03/04 organization discovery", () => {
  it('withdraws cached names and creation consent when live admission is withdrawn', async () => {
    let withdrawn = false;
    stubFetch(vi.fn(async (path: string) => path === '/organizations' ? response(organizations)
      : withdrawn ? response({}, 404) : response([{ id: 'private-board', name: 'Private current Board', version: 1 }])));
    mount('/app/org-1'); await screen.findByRole('link', { name: 'Private current Board' });
    await waitFor(() => expect(live.watch).toHaveBeenCalledWith(expect.objectContaining({ userId: profile.id, audience: 'discovery' })));
    fireEvent.click(screen.getByRole('button', { name: 'Create board' }));
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    withdrawn = true; act(() => live.watch.mock.calls[0][0].reset());
    expect(screen.queryByRole('link', { name: 'Private current Board' })).not.toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    await screen.findByRole('alert'); expect(screen.queryByText('Council')).not.toBeInTheDocument();
  });
  it('fences an older directory response after a newer canonical invalidation', async () => {
    let reads = 0; let finish!: (value: Response) => void; let oldSignal: AbortSignal | null | undefined;
    stubFetch(vi.fn(async (path: string, options?: RequestInit) => {
      if (path === '/organizations') return response(organizations);
      if (++reads === 2) { oldSignal = options?.signal; return new Promise<Response>(resolve => { finish = resolve; }); }
      return response([{ id: 'current-board', name: reads === 1 ? 'Original Board' : 'New current Board', version: reads }]);
    }));
    mount('/app/org-1'); await screen.findByRole('link', { name: 'Original Board' });
    await waitFor(() => expect(live.watch).toHaveBeenCalledTimes(1));
    act(() => live.watch.mock.calls[0][0].invalidate());
    await waitFor(() => expect(finish).toBeDefined());
    act(() => live.watch.mock.calls[0][0].invalidate());
    expect(oldSignal?.aborted).toBe(true);
    await screen.findByRole('link', { name: 'New current Board' });
    await act(async () => finish(response([{ id: 'old-board', name: 'Withdrawn old Board', version: 1 }])));
    expect(screen.queryByText('Withdrawn old Board')).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'New current Board' })).toBeInTheDocument();
  });
  it('withholds names if the current account changes during directory IO', async () => {
    let profiles = 0;
    vi.stubGlobal('fetch', vi.fn(async (path: string) => path === '/me'
      ? response({ ...profile, id: ++profiles === 1 ? profile.id : '33333333-3333-4333-8333-333333333333' })
      : path === '/organizations' ? response(organizations) : response([{ id: 'board', name: 'Previous account Board', version: 1 }])));
    mount('/app/org-1'); await screen.findByText('Sign in destination');
    expect(screen.queryByText('Previous account Board')).not.toBeInTheDocument();
    expect(screen.queryByText('Council')).not.toBeInTheDocument(); expect(live.watch).not.toHaveBeenCalled();
  });
  it("displays an honest empty state and creation action for a new account", async () => {
    stubFetch(vi.fn().mockResolvedValue(response([])));
    mount();
    expect(
      await screen.findByText(/You have no organizations yet/),
    ).toBeVisible();
    expect(
      screen.getByRole("button", { name: "Create organization" }),
    ).toBeVisible();
    expect(screen.queryByText("demo")).not.toBeInTheDocument();
  });
  it("creates an organization and opens its authorized board list", async () => {
    const fetcher = vi
      .fn()
      .mockResolvedValueOnce(response([]))
      .mockResolvedValueOnce(response(organizations[0], 201))
      .mockResolvedValueOnce(response(organizations))
      .mockResolvedValueOnce(response([]));
    stubFetch(fetcher);
    const router = mount();
    fireEvent.click(
      await screen.findByRole("button", { name: "Create organization" }),
    );
    fireEvent.change(screen.getByLabelText(/Name/), {
      target: { value: "Council" },
    });
    fireEvent.click(screen.getByRole("button", { name: /^Create$/ }));
    expect(
      await screen.findByRole("heading", { name: "Council" }),
    ).toBeVisible();
    expect(router.state.location.pathname).toBe("/app/org-1");
    expect(JSON.parse(fetcher.mock.calls[1][1].body)).toEqual({
      name: "Council",
      description: "",
    });
    expect(fetcher.mock.calls[1][1].headers.get("X-StrataAI-Request")).toBe(
      "1",
    );
  });
  it("defaults new boards to private and opens the persisted board ID", async () => {
    const fetcher = vi
      .fn()
      .mockResolvedValueOnce(response(organizations))
      .mockResolvedValueOnce(response([]))
      .mockResolvedValueOnce(response({ id: "new-board" }, 201));
    stubFetch(fetcher);
    const router = mount("/app/org-1");
    fireEvent.click(
      await screen.findByRole("button", { name: "Create board" }),
    );
    fireEvent.change(screen.getByLabelText(/Name/), {
      target: { value: "Planning" },
    });
    fireEvent.click(screen.getByRole("button", { name: /^Create$/ }));
    await screen.findByText("Created board destination");
    expect(router.state.location.pathname).toBe("/app/org-1/boards/new-board");
    expect(JSON.parse(fetcher.mock.calls[2][1].body)).toMatchObject({
      name: "Planning",
      organizationId: "org-1",
      visibility: "PRIVATE",
      backgroundType: "COLOR",
      backgroundValue: "blue",
    });
  });
  it("redirects expired sessions to sign in", async () => {
    stubFetch(vi.fn().mockResolvedValue(response({}, 401)));
    mount();
    expect(await screen.findByText("Sign in destination")).toBeVisible();
  });
  it("does not request or expose boards for an organization outside active membership", async () => {
    const fetcher = vi.fn().mockResolvedValue(response(organizations));
    stubFetch(fetcher);
    mount("/app/another-org");
    expect(await screen.findByRole("alert")).toHaveTextContent("unavailable");
    expect(fetcher).toHaveBeenCalledTimes(1);
    expect(screen.queryByText("Council")).not.toBeInTheDocument();
  });
  it("clears organization names immediately when switching scope", async () => {
    const fetcher = vi
      .fn()
      .mockResolvedValueOnce(response(organizations))
      .mockResolvedValueOnce(
        response([
          {
            organization: { id: "org-2", name: "Other council", status: 0 },
            role: 2,
          },
        ]),
      )
      .mockResolvedValueOnce(
        response([{ id: "board-2", name: "Accessible board", version: 1 }]),
      );
    stubFetch(fetcher);
    const router = mount();
    await screen.findByText("Council");
    await router.navigate("/app/org-2");
    await waitFor(() =>
      expect(
        screen.getByRole("link", { name: "Accessible board" }),
      ).toHaveAttribute("href", "/app/org-2/boards/board-2"),
    );
    expect(screen.queryByText("Council")).not.toBeInTheDocument();
  });
});
