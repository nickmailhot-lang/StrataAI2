import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { ArchivedBoardsPage } from './ArchivedBoardsPage';
import { configureActivityTelemetry, flushActivityTelemetry } from './activityTelemetry';
import { watchOrganizationBoards } from './organizationBoardLive';
vi.mock('./organizationBoardLive', () => ({ watchOrganizationBoards: vi.fn(() => () => {}) }));
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
afterEach(() => { configureActivityTelemetry(false); vi.unstubAllGlobals(); vi.useRealTimers(); vi.clearAllMocks(); });
it('withdraws cached names immediately on live reset and fences the previous read', async () => {
  let finish!: (value: Response) => void; let older!: (value: Response) => void; let oldSignal: AbortSignal | undefined; let reads = 0;
  const fetch = vi.fn((path: string, init: RequestInit) => {
    if (path === '/me') return Promise.resolve(response(profile));
    if (++reads === 1) return Promise.resolve(response(page));
    if (reads === 2) { oldSignal = init.signal!; return new Promise<Response>(resolve => { older = resolve; }); }
    return new Promise<Response>(resolve => { finish = resolve; });
  });
  mount(fetch); await screen.findByRole('article', { name: 'Planning' });
  await waitFor(() => expect(watchOrganizationBoards).toHaveBeenCalled());
  const callbacks = vi.mocked(watchOrganizationBoards).mock.calls.at(-1)![0];
  fireEvent.click(screen.getByRole('button', { name: 'Check current archived boards' }));
  await waitFor(() => expect(older).toBeDefined());
  act(() => callbacks.reset());
  expect(oldSignal!.aborted).toBe(true);
  expect(screen.getByText('Checking current archive access.')).toHaveAttribute('aria-live', 'polite');
  expect(screen.queryByRole('article')).not.toBeInTheDocument(); expect(screen.queryByText('Planning')).not.toBeInTheDocument();
  await waitFor(() => expect(finish).toBeDefined());
  await act(async () => finish(response({ ...page, items: [] })));
  await screen.findByText('No administrable archived Boards on this page.');
  expect(screen.getByText('Current archived boards checked.')).toHaveAttribute('aria-atomic', 'true');
  // A canceled network peer may still return. Its private snapshot must not
  // replace the later admitted empty directory or resurrect stale consent.
  await act(async () => older(response(page)));
  expect(screen.queryByRole('article')).not.toBeInTheDocument();
  expect(screen.getByText('No administrable archived Boards on this page.')).toBeVisible();
});
it('disables retired restore consent during dialog exit even after fresh archive access is confirmed', async () => {
  const fetch = vi.fn(async (path: string, init: RequestInit) => response(path === '/me' ? profile
    : init.method === 'POST' ? { ...board, version: 3, lifecycleState: 'active' } : page));
  mount(fetch); fireEvent.click(await screen.findByRole('button', { name: 'Restore Planning board' }));
  const confirm = await screen.findByRole('button', { name: 'Confirm restore' });
  await waitFor(() => expect(watchOrganizationBoards).toHaveBeenCalled());
  vi.useFakeTimers();
  await act(async () => vi.mocked(watchOrganizationBoards).mock.calls.at(-1)![0].reset());
  expect(screen.getByText('Current archived boards checked.')).toBeInTheDocument();
  expect(confirm).toBeDisabled(); fireEvent.click(confirm);
  expect(fetch.mock.calls.filter(call => call[1].method === 'POST')).toHaveLength(0);
  await act(async () => vi.advanceTimersByTimeAsync(500));
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  await act(async () => fireEvent.click(screen.getByRole('button', { name: 'Restore Planning board' })));
  const fresh = screen.getByRole('button', { name: 'Confirm restore' }); expect(fresh).toBeEnabled();
  await act(async () => fireEvent.click(fresh));
  expect(fetch.mock.calls.filter(call => call[1].method === 'POST')).toHaveLength(1);
});
it('keeps the original unconfirmed key while a live reset withholds its private review', async () => {
  let writes = 0;
  const fetch = vi.fn((path: string, init: RequestInit) => {
    if (path === '/me') return Promise.resolve(response(profile));
    if (init.method === 'POST') return Promise.resolve(++writes === 1 ? response({ detail: 'Private failure' }, 503)
      : response({ ...board, version: 3, lifecycleState: 'active' }));
    return Promise.resolve(response(writes ? { ...page, items: [] } : page));
  });
  mount(fetch); fireEvent.click(await screen.findByRole('button', { name: 'Restore Planning board' }));
  fireEvent.click(screen.getByRole('button', { name: 'Confirm restore' }));
  await screen.findByRole('button', { name: 'Retry this change' });
  await waitFor(() => expect(watchOrganizationBoards).toHaveBeenCalled());
  act(() => vi.mocked(watchOrganizationBoards).mock.calls.at(-1)![0].reset());
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  expect(screen.queryByText('Planning')).not.toBeInTheDocument();
  const retry = await screen.findByRole('button', { name: 'Retry this change' }); await waitFor(() => expect(retry).toBeEnabled());
  retry.focus(); fireEvent.click(retry); await screen.findByText('Board restore acknowledged.');
  await waitFor(() => expect(screen.getByRole('button', { name: 'Check current archived boards' })).toHaveFocus());
  const commands = fetch.mock.calls.filter(call => call[1].method === 'POST');
  expect(commands).toHaveLength(2);
  expect(new Headers(commands[0][1].headers).get('Idempotency-Key')).toBe(new Headers(commands[1][1].headers).get('Idempotency-Key'));
});
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

async function observations(fetch: ReturnType<typeof vi.fn>) {
  await flushActivityTelemetry();
  const reports = fetch.mock.calls.filter(call => call[0] === '/me/activity-client-events');
  expect(reports).toHaveLength(1);
  const value = JSON.parse(reports[0][1].body);
  for (const secret of [org, id, user, board.name, board.archivedAt, 'Private failure', 'Idempotency-Key'])
    expect(JSON.stringify(value)).not.toContain(secret);
  for (const event of value.events) expect(Object.keys(event).sort()).toEqual(event.durationMs === undefined
    ? ['action', 'count', 'kind'] : ['action', 'count', 'durationMs', 'kind']);
  return value.events as { action: string; kind: string; count: number; durationMs?: number }[];
}
it('observes Board deletion retry after canonical removal without retaining private material', async () => {
  configureActivityTelemetry(true); let writes = 0;
  const fetch = vi.fn((path: string, init: RequestInit) => {
    if (path === '/me/activity-client-events') return Promise.resolve(new Response(null, { status: 204 }));
    if (path === '/me') return Promise.resolve(response(profile));
    if (init.method === 'DELETE') return Promise.resolve(++writes === 1 ? response({ detail: 'Private failure' }, 503)
      : response({ ...board, version: 3, lifecycleState: 'deleted', deletedBy: user }));
    return Promise.resolve(response(writes ? { ...page, items: [] } : page));
  });
  mount(fetch); fireEvent.click(await screen.findByRole('button', { name: 'Permanently delete Planning board' }));
  fireEvent.click(screen.getByRole('checkbox', { name: 'I understand this cannot be undone.' }));
  fireEvent.click(screen.getByRole('button', { name: 'Confirm permanent deletion' }));
  const retry = await screen.findByRole('button', { name: 'Retry this change' }); await waitFor(() => expect(retry).toBeEnabled());
  fireEvent.click(retry); await screen.findByText('No administrable archived Boards on this page.');
  const events = await observations(fetch);
  expect(events).toContainEqual({ action: 'archive_board_disclosure', kind: 'open', count: 1 });
  for (const kind of ['open', 'use', 'retry', 'exception']) expect(events).toContainEqual({ action: 'archive_board_delete', kind, count: 1 });
  for (const kind of ['success', 'failure']) expect(events).toContainEqual({ action: 'archive_board_delete', kind, count: 1, durationMs: expect.any(Number) });
  const commands = fetch.mock.calls.filter(call => call[1].method === 'DELETE');
  expect(new Headers(commands[0][1].headers).get('Idempotency-Key')).toBe(new Headers(commands[1][1].headers).get('Idempotency-Key'));
});
it('observes Board restore conflicts as failure and foreground denial as recovery', async () => {
  configureActivityTelemetry(true); let denied = false;
  const fetch = vi.fn((path: string, init: RequestInit) => {
    if (path === '/me/activity-client-events') return Promise.resolve(new Response(null, { status: 204 }));
    if (path === '/me') return Promise.resolve(response(profile));
    return Promise.resolve(init.method === 'POST' ? response({ detail: 'Private failure' }, 409)
      : denied ? response({ detail: 'Private failure' }, 403) : response(page));
  });
  mount(fetch); fireEvent.click(await screen.findByRole('button', { name: 'Restore Planning board' }));
  fireEvent.click(screen.getByRole('button', { name: 'Confirm restore' }));
  await screen.findByText('This change could not be applied. Cancel and review the current archive.');
  await waitFor(() => expect(screen.getByRole('button', { name: 'Check current archive for this change' })).toBeEnabled());
  denied = true; fireEvent(document, new Event('visibilitychange'));
  await screen.findByText('Unable to confirm current Board archive access. Check again before continuing.');
  const events = await observations(fetch);
  expect(events).toContainEqual({ action: 'archive_board_restore', kind: 'conflict', count: 1 });
  expect(events).toContainEqual({ action: 'archive_board_restore', kind: 'failure', count: 1, durationMs: expect.any(Number) });
  expect(events.some(event => event.action === 'archive_board_restore' && event.kind === 'success')).toBe(false);
  expect(events).toContainEqual({ action: 'archive_board_read', kind: 'reconnect', count: 1 });
  expect(events).toContainEqual({ action: 'archive_board_read', kind: 'failure', count: 1, durationMs: expect.any(Number) });
});
it('preserves Board reconnect classification while directory invalidations coalesce', async () => {
  configureActivityTelemetry(true); let reads = 0; let finish!: (value: Response) => void;
  const fetch = vi.fn((path: string) => {
    if (path === '/me/activity-client-events') return Promise.resolve(new Response(null, { status: 204 }));
    if (path === '/me') return Promise.resolve(response(profile));
    if (++reads === 2) return new Promise<Response>(resolve => { finish = resolve; });
    return Promise.resolve(response(page));
  });
  mount(fetch); await screen.findByRole('button', { name: 'Restore Planning board' });
  fireEvent.click(screen.getByRole('button', { name: 'Check current archived boards' }));
  await waitFor(() => expect(finish).toBeDefined());
  fireEvent(window, new Event('online')); fireEvent(document, new Event('visibilitychange'));
  expect(reads).toBe(2); await act(async () => finish(response(page)));
  await waitFor(() => expect(reads).toBe(3));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Check current archived boards' })).toBeEnabled());
  const events = await observations(fetch);
  expect(events).toContainEqual({ action: 'archive_board_read', kind: 'retry', count: 1 });
  expect(events).toContainEqual({ action: 'archive_board_read', kind: 'reconnect', count: 1 });
});

it.each([false, true])('withdraws consent on an account switch %s after command submission', async after => {
  let checks = 0; let writes = 0;
  const fetch = vi.fn(async (path: string, init: RequestInit) => {
    if (path === '/me') return response(++checks === (after ? 4 : 3) ? { ...profile, id: org } : profile);
    if (init.method === 'POST') { writes++; return response({ ...board, version: 3, lifecycleState: 'active' }); }
    return response(page);
  });
  mount(fetch); fireEvent.click(await screen.findByRole('button', { name: 'Restore Planning board' }));
  fireEvent.click(screen.getByRole('button', { name: 'Confirm restore' }));
  await screen.findByText('Board administration is unavailable.');
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  expect(writes).toBe(after ? 1 : 0); expect(screen.queryByRole('article')).not.toBeInTheDocument();
  expect(screen.queryByText('Board restore acknowledged.')).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Retry this change' })).not.toBeInTheDocument();
});
it.each([false, true])('withholds uncertain account confirmation %s after submission and recovers only a submitted key', async after => {
  let checks = 0; let writes = 0;
  const fetch = vi.fn(async (path: string, init: RequestInit) => {
    if (path === '/me') return ++checks === (after ? 4 : 3) ? response({}, 503) : response(profile);
    if (init.method === 'POST') { writes++; return response({ ...board, version: 3, lifecycleState: 'active' }); }
    return response(writes ? { ...page, items: [] } : page);
  });
  mount(fetch); fireEvent.click(await screen.findByRole('button', { name: 'Restore Planning board' }));
  fireEvent.click(screen.getByRole('button', { name: 'Confirm restore' }));
  await screen.findByText(after
    ? 'This change is unconfirmed. Check current archived boards, then retry the same request to recover its acknowledgment.'
    : 'Unable to confirm the current account. No Board change was sent. Check current archived boards before reviewing again.');
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  expect(writes).toBe(after ? 1 : 0); expect(screen.queryByRole('article')).not.toBeInTheDocument();
  expect(screen.queryByText('Board restore acknowledged.')).not.toBeInTheDocument();
  if (after) expect(screen.getByRole('button', { name: 'Retry this change' })).toBeDisabled();
  else expect(screen.queryByRole('button', { name: 'Retry this change' })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Check current archived boards' }));
  if (after) {
    const retry = screen.getByRole('button', { name: 'Retry this change' }); await waitFor(() => expect(retry).toBeEnabled());
    fireEvent.click(retry); await screen.findByText('Board restore acknowledged.');
    const commands = fetch.mock.calls.filter(call => call[1].method === 'POST'); expect(commands).toHaveLength(2);
    expect(new Headers(commands[0][1].headers).get('Idempotency-Key')).toBe(new Headers(commands[1][1].headers).get('Idempotency-Key'));
    expect(new Headers(commands[0][1].headers).get('X-StrataAI-Expected-Actor')).toBe(user);
  } else { await screen.findByRole('article', { name: 'Planning' }); expect(writes).toBe(0); }
});

it('sends no command when live admission withdraws consent during the account check', async () => {
  let checks = 0; let finish!: (value: Response) => void;
  const fetch = vi.fn((path: string, _init: RequestInit) => {
    if (path === '/me') return ++checks === 3 ? new Promise<Response>(resolve => { finish = resolve; }) : Promise.resolve(response(profile));
    return Promise.resolve(response(page));
  });
  mount(fetch); fireEvent.click(await screen.findByRole('button', { name: 'Restore Planning board' }));
  fireEvent.click(screen.getByRole('button', { name: 'Confirm restore' })); await waitFor(() => expect(finish).toBeDefined());
  act(() => vi.mocked(watchOrganizationBoards).mock.calls.at(-1)![0].reset());
  await act(async () => finish(response(profile)));
  await screen.findByText('Unable to confirm the current account. No Board change was sent. Check current archived boards before reviewing again.');
  expect(fetch.mock.calls.filter(call => call[1]?.method === 'POST')).toHaveLength(0);
});
it.each([false, true])('bounds noncooperative account checks %s after submission and fences their late replies', async after => {
  let checks = 0; let finish!: (value: Response) => void; let signal!: AbortSignal; let writes = 0;
  const fetch = vi.fn((path: string, init: RequestInit) => {
    if (path === '/me') {
      if (++checks === (after ? 4 : 3)) { signal = init.signal!; return new Promise<Response>(resolve => { finish = resolve; }); }
      return Promise.resolve(response(profile));
    }
    if (init.method === 'POST') { writes++; return Promise.resolve(response({ ...board, version: 3, lifecycleState: 'active' })); }
    return Promise.resolve(response(page));
  });
  mount(fetch); fireEvent.click(await screen.findByRole('button', { name: 'Restore Planning board' }));
  vi.useFakeTimers(); await act(async () => fireEvent.click(screen.getByRole('button', { name: 'Confirm restore' })));
  expect(finish).toBeDefined(); await act(async () => vi.advanceTimersByTimeAsync(15_001));
  expect(signal.aborted).toBe(true); expect(writes).toBe(after ? 1 : 0);
  await act(async () => vi.advanceTimersByTimeAsync(500));
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument(); expect(screen.queryByRole('article')).not.toBeInTheDocument();
  await act(async () => finish(response(profile)));
  expect(writes).toBe(after ? 1 : 0); expect(screen.queryByText('Board restore acknowledged.')).not.toBeInTheDocument();
  if (after) expect(screen.getByRole('button', { name: 'Retry this change' })).toBeDisabled();
  else expect(screen.queryByRole('button', { name: 'Retry this change' })).not.toBeInTheDocument();
});
