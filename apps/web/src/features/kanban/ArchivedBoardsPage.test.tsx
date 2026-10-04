import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { ArchivedBoardsPage } from './ArchivedBoardsPage';
const org = '10000000-0000-4000-8000-000000000001', id = '20000000-0000-4000-8000-000000000001', user = '30000000-0000-4000-8000-000000000001';
const profile = { id: user, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'America/Vancouver' };
const board = { id, organizationId: org, name: 'Planning', version: 2, archivedAt: '2026-10-04T12:00:00Z' };
const page = { organizationId: org, items: [board], nextCursor: null };
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
function mount(fetch: ReturnType<typeof vi.fn>) {
  vi.stubGlobal('fetch', fetch);
  return render(<RouterProvider router={createMemoryRouter([{ path: '/app/:organizationId/archived-boards', element: <ArchivedBoardsPage /> }],
    { initialEntries: [`/app/${org}/archived-boards`] })} />);
}
afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); });
it.each([
  { ...page, organizationId: user },
  { ...page, items: [{ ...board, organizationId: user }] },
  { ...page, items: [board, board] },
  { ...page, items: [{ ...board, description: 'Private body' }] },
  { ...page, items: [{ ...board, archivedAt: '2026-02-30T12:00:00Z' }] },
  { ...page, nextCursor: id },
])('withholds malformed archive scope, ordering, metadata or cursor', async value => {
  const fetch = vi.fn((path: string) => Promise.resolve(response(path === '/me' ? profile : value)));
  mount(fetch); await screen.findByText('Unable to confirm current Board archive access. Check again before continuing.');
  expect(screen.queryByRole('article')).not.toBeInTheDocument();
  expect(screen.queryByText('Private body')).not.toBeInTheDocument();
});
it('reviews a reversible restore and reflects only its validated acknowledgment and fresh directory', async () => {
  let restored = false;
  const fetch = vi.fn((path: string, init: RequestInit) => {
    if (path === '/me') return Promise.resolve(response(profile));
    if (init.method === 'POST') { restored = true; return Promise.resolve(response({ ...board, version: 3, lifecycleState: 'active' })); }
    return Promise.resolve(response(restored ? { ...page, items: [] } : page));
  });
  mount(fetch); fireEvent.click(await screen.findByRole('button', { name: 'Restore Planning board' }));
  fireEvent.click(screen.getByRole('button', { name: 'Confirm restore' }));
  await screen.findByText('No administrable archived Boards on this page.');
  const write = fetch.mock.calls.find(call => call[1].method === 'POST')!;
  expect(write[0]).toBe(`/boards/${id}/restore`); expect(JSON.parse(write[1].body as string)).toEqual({ version: 2 });
  expect(new Headers(write[1].headers).get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
  expect(screen.getByText('Board restore acknowledged.')).toBeVisible();
  await waitFor(() => expect(screen.getByRole('button', { name: 'Check current archived boards' })).toHaveFocus());
});
it('withdraws an unresolved review when account identity changes across a directory read', async () => {
  let accounts = 0; let writes = 0;
  const fetch = vi.fn((path: string, init: RequestInit) => {
    if (path === '/me') return Promise.resolve(response(++accounts === 4 ? { ...profile, id: '40000000-0000-4000-8000-000000000001' } : profile));
    if (init.method === 'POST') { writes++; return Promise.resolve(response({ detail: 'Private failure' }, 503)); }
    return Promise.resolve(response(page));
  });
  mount(fetch); fireEvent.click(await screen.findByRole('button', { name: 'Restore Planning board' }));
  fireEvent.click(screen.getByRole('button', { name: 'Confirm restore' }));
  await screen.findByText('Unable to confirm current Board archive access. Check again before continuing.');
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  expect(screen.queryByRole('article')).not.toBeInTheDocument(); expect(writes).toBe(1);
});
it('requires irreversible consent and retries the original deletion after canonical removal', async () => {
  let writes = 0;
  const fetch = vi.fn((path: string, init: RequestInit) => {
    if (path === '/me') return Promise.resolve(response(profile));
    if (init.method === 'DELETE') return Promise.resolve(++writes === 1 ? response({ detail: 'Private failure' }, 503)
      : response({ ...board, version: 3, lifecycleState: 'deleted', deletedBy: user }));
    return Promise.resolve(response(writes ? { ...page, items: [] } : page));
  });
  mount(fetch); fireEvent.click(await screen.findByRole('button', { name: 'Permanently delete Planning board' }));
  expect(screen.getByRole('button', { name: 'Confirm permanent deletion' })).toBeDisabled();
  expect(screen.getByText(/This cannot be undone. This Board cannot be restored/)).toBeVisible();
  fireEvent.click(screen.getByRole('checkbox', { name: 'I understand this cannot be undone.' }));
  fireEvent.click(screen.getByRole('button', { name: 'Confirm permanent deletion' }));
  const retry = await screen.findByRole('button', { name: 'Retry this change' }); await waitFor(() => expect(retry).toBeEnabled());
  expect(screen.queryByText('Private failure')).not.toBeInTheDocument(); fireEvent.click(retry);
  await screen.findByText('Board deletion acknowledged.');
  const commands = fetch.mock.calls.filter(call => call[1].method === 'DELETE');
  expect(commands).toHaveLength(2); expect(commands[0][0]).toBe(`/boards/${id}?version=2&confirmed=true`);
  expect(new Headers(commands[0][1].headers).get('Idempotency-Key')).toBe(new Headers(commands[1][1].headers).get('Idempotency-Key'));
});
it('retires a pending write after online permission denial and ignores its late acknowledgment', async () => {
  let denied = false; let finish!: (value: Response) => void;
  const fetch = vi.fn((path: string, init: RequestInit) => {
    if (path === '/me') return Promise.resolve(response(profile));
    if (init.method === 'POST') return new Promise<Response>(resolve => { finish = resolve; });
    return Promise.resolve(denied ? response({ detail: 'Private failure' }, 403) : response(page));
  });
  const view = mount(fetch); fireEvent.click(await screen.findByRole('button', { name: 'Restore Planning board' }));
  fireEvent.click(screen.getByRole('button', { name: 'Confirm restore' })); await waitFor(() => expect(finish).toBeDefined());
  denied = true; fireEvent(window, new Event('online'));
  await screen.findByText('Unable to confirm current Board archive access. Check again before continuing.');
  await act(async () => finish(response({ ...board, version: 3, lifecycleState: 'active' })));
  expect(screen.queryByText('Board restore acknowledged.')).not.toBeInTheDocument();
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument()); expect(screen.queryByRole('article')).not.toBeInTheDocument();
  const count = fetch.mock.calls.length; view.unmount(); fireEvent(window, new Event('online')); await act(async () => {});
  expect(fetch).toHaveBeenCalledTimes(count);
});
