import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";

import { App } from "./App";
const lifecycle = vi.hoisted(() => ({ watch: vi.fn<(options: {
  organizationId: string; userId: string; update(state: 'ACTIVE' | 'PENDING' | 'COMPLETED'): void;
  unavailable(): void; accountUnavailable(): void;
}) => () => void>(() => vi.fn()) }));
vi.mock('../features/organizations/organizationLifecycleLive', () => ({ watchOrganizationLifecycle: lifecycle.watch }));
beforeEach(() => lifecycle.watch.mockClear());

afterEach(() => { vi.unstubAllGlobals(); sessionStorage.clear(); });

describe("StrataAI2 application shell", () => {
  it("renders the internal application shell", async () => {
    window.history.pushState({}, "", "/app/demo/boards/demo-board");
    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation((path: string) => Promise.resolve(path.includes('/surface-access')
        ? new Response(JSON.stringify({ organizationId: 'demo', surface: 'INTERNAL' })) :
        new Response(
          JSON.stringify({
            board: {
              id: "demo-board",
              organizationId: "demo",
              name: "Council Operations",
              lifecycleState: "active",
            },
            lists: [],
            access: { canView: true, canEdit: false },
          }),
        ),
      )),
    );

    render(<App />);

    expect(
      await screen.findByRole("heading", { name: "Council Operations" }),
    ).toBeInTheDocument();
    expect(screen.getByText(/Organization: demo/)).toBeInTheDocument();
    const navigation = screen.getByRole('navigation', { name: 'Internal application navigation' });
    expect(within(navigation).getByRole('link', { name: 'Organizations' })).toHaveAttribute('href', '/app');
    expect(within(navigation).getByRole('link', { name: 'Boards' })).toHaveAttribute('href', '/app/demo');
  });

  it("keeps the owner portal visually and navigationally separate", async () => {
    window.history.pushState({}, "", "/portal/demo");
    vi.stubGlobal('fetch', vi.fn().mockImplementation(() => Promise.resolve(new Response(JSON.stringify({ organizationId: 'demo', surface: 'PORTAL' })))));

    render(<App />);

    expect(
      await screen.findByRole("heading", { name: "Owner documents" }),
    ).toBeInTheDocument();
    expect(screen.queryByText("Council Operations")).not.toBeInTheDocument();
  });
});
vi.mock("../api/boardLive", () => ({ watchBoard: vi.fn(() => () => {}) }));

it('retains deletion recovery after normal surface access is withdrawn', async () => {
  const org = '55555555-5555-4555-8555-555555555555'; const actor = '22222222-2222-4222-8222-222222222222';
  const profile = { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'UTC' };
  let writes = 0;
  const fetcher = vi.fn(async (path: string, options?: RequestInit) => {
    if (path === '/me') return new Response(JSON.stringify(profile));
    if (path.includes('/surface-access')) return new Response('{}', { status: 404 });
    if (options?.method === 'DELETE') { if (++writes === 1) throw new TypeError('Lost response after commit'); return new Response(null, { status: 202 }); }
    return new Response(JSON.stringify({ organization: { id: org, name: 'Original deletion review', status: 0, version: 1 }, role: 0 }));
  });
  vi.stubGlobal('fetch', fetcher); window.history.pushState({}, '', `/app/${org}/delete`); render(<App />);
  fireEvent.click(await screen.findByRole('button', { name: 'Review deletion request' }));
  fireEvent.click(screen.getByRole('button', { name: 'Confirm deletion request' }));
  const retry = await screen.findByRole('button', { name: 'Retry original deletion request' }); await waitFor(() => expect(retry).toHaveFocus());
  vi.useFakeTimers(); try { await act(async () => { await vi.advanceTimersByTimeAsync(10_001); }); } finally { vi.useRealTimers(); }
  expect(retry).toBeInTheDocument(); expect(retry).toBeEnabled(); fireEvent.click(retry);
  await screen.findByText('Deletion request acknowledged. Deletion has not been confirmed complete.');
  expect(fetcher.mock.calls.some(([path]) => path.includes('/surface-access'))).toBe(false);
  const calls = fetcher.mock.calls.filter(([, options]) => options?.method === 'DELETE'); expect(calls).toHaveLength(2);
  expect(calls[0][0]).toBe(calls[1][0]);
  expect((calls[0][1]!.headers as Headers).get('Idempotency-Key')).toBe((calls[1][1]!.headers as Headers).get('Idempotency-Key'));
});

// PRD-03-TC-09/10 / PRD-18: exercise the actual router and shell boundary,
// because a component-only lifecycle test cannot prove denied surface mounting.
it('admits protected terminal recovery on a fresh Organization route after ordinary surface withdrawal', async () => {
  const org = '55555555-5555-4555-8555-555555555555';
  const actor = '22222222-2222-4222-8222-222222222222';
  const profile = { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'UTC' };
  const fetcher = vi.fn(async (path: string) => path === '/me'
    ? new Response(JSON.stringify(profile)) : new Response('{}', { status: 404 }));
  vi.stubGlobal('fetch', fetcher); window.history.pushState({}, '', `/app/${org}`); render(<App />);
  await waitFor(() => expect(lifecycle.watch).toHaveBeenCalledWith(expect.objectContaining({ organizationId: org, userId: actor })));
  act(() => lifecycle.watch.mock.calls[0][0].update('COMPLETED'));
  expect(await screen.findByRole('status')).toHaveTextContent('Organization deletion confirmed complete.');
  expect(screen.queryByRole('button', { name: 'Create board' })).not.toBeInTheDocument();
  expect(screen.queryByRole('navigation', { name: 'Internal application navigation' })).not.toBeInTheDocument();
  expect(fetcher.mock.calls.some(([path]) => path.includes('/boards/directory'))).toBe(false);
});
