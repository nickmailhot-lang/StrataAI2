import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { ArchivedListsPage } from './ArchivedListsPage';
import { ArchivedCardsPage } from './ArchivedCardsPage';
import { configureActivityTelemetry, flushActivityTelemetry } from './activityTelemetry';
import { watchBoard } from '../../api/boardLive';

vi.mock('../../api/boardLive', () => ({ watchBoard: vi.fn(() => () => {}) }));
const org = '10000000-0000-4000-8000-000000000001', board = '10000000-0000-4000-8000-000000000002';
const list = { id: '20000000-0000-4000-8000-000000000001', organizationId: org, boardId: board,
  name: 'Private planning', rank: '500', version: 1, lifecycleState: 'archived' };
const card = { id: '30000000-0000-4000-8000-000000000001', organizationId: org, boardId: board, listId: list.id,
  title: 'Private budget', description: null, rank: '500', version: 1, lifecycleState: 'archived' };
const reply = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
const cases = ['list', 'card'] as const;
function fixture(kind: typeof cases[number]) {
  const page = { organizationId: org, boardId: board, nextCursor: null, canDelete: true,
    items: kind === 'list' ? [{ list, containedCardCount: 2 }] : [{ card, list: { ...list, lifecycleState: 'active' } }] };
  const item = kind === 'list' ? list : card;
  const restoreName = kind === 'list' ? 'Restore Private planning list' : 'Restore Private budget card';
  const deleteName = kind === 'list' ? 'Permanently delete Private planning list' : 'Permanently delete Private budget card';
  function mount(fetch: ReturnType<typeof vi.fn>) {
    vi.stubGlobal('fetch', fetch);
    const router = createMemoryRouter([{ path: '/app/:organizationId/boards/:boardId/archived-' + kind + 's',
      element: kind === 'list' ? <ArchivedListsPage /> : <ArchivedCardsPage /> }],
    { initialEntries: [`/app/${org}/boards/${board}/archived-${kind}s`] });
    return render(<RouterProvider router={router} />);
  }
  return { page, item, restoreName, deleteName, mount };
}
afterEach(() => { configureActivityTelemetry(false); vi.unstubAllGlobals(); vi.clearAllMocks(); });
async function observations(fetch: ReturnType<typeof vi.fn>) {
  await flushActivityTelemetry();
  const reports = fetch.mock.calls.filter(call => call[0] === '/me/activity-client-events');
  expect(reports).toHaveLength(1);
  const body = JSON.parse(reports[0][1].body);
  const payload = JSON.stringify(body);
  for (const secret of [org, board, list.id, card.id, list.name, card.title, 'Private error', 'Idempotency-Key', 'containedCardCount'])
    expect(payload).not.toContain(secret);
  for (const event of body.events) expect(Object.keys(event).sort()).toEqual(
    event.durationMs === undefined ? ['action', 'count', 'kind'] : ['action', 'count', 'durationMs', 'kind']);
  return body.events as { action: string; kind: string; count: number; durationMs?: number }[];
}
it.each(cases)('reports %s restore recovery and online denial without private material', async kind => {
  configureActivityTelemetry(true);
  const f = fixture(kind); let writes = 0; let reads = 0;
  const fetch = vi.fn((path: string, init: RequestInit) => {
    if (path === '/me/activity-client-events') return Promise.resolve(new Response(null, { status: 204 }));
    if (init.method === 'POST') return Promise.resolve(++writes === 1 ? reply({ detail: 'Private error' }, 503)
      : reply({ ...f.item, version: 2, lifecycleState: 'active' }));
    reads++;
    return Promise.resolve(reads === 4 ? reply({ detail: 'Private error' }, 403) : reply(reads === 3 ? { ...f.page, items: [] } : f.page));
  });
  f.mount(fetch); fireEvent.click(await screen.findByRole('button', { name: f.restoreName }));
  fireEvent.click(screen.getByRole('button', { name: 'Confirm restore' }));
  const retry = await screen.findByRole('button', { name: 'Retry this restore' });
  await waitFor(() => expect(retry).toBeEnabled()); fireEvent.click(retry);
  await screen.findByText(`No archived ${kind}s on this page.`);
  fireEvent(window, new Event('online'));
  await screen.findByText(kind === 'list' ? 'Archived List administration is unavailable.' : 'Archived Card access is unavailable.');
  const events = await observations(fetch);
  expect(events).toContainEqual({ action: `archive_${kind}_disclosure`, kind: 'open', count: 1 });
  for (const event of ['open', 'use', 'retry', 'exception']) expect(events).toContainEqual({ action: `archive_${kind}_restore`, kind: event, count: 1 });
  for (const event of ['success', 'failure']) expect(events).toContainEqual({ action: `archive_${kind}_restore`, kind: event, count: 1, durationMs: expect.any(Number) });
  expect(events).toContainEqual({ action: `archive_${kind}_read`, kind: 'reconnect', count: 1 });
  expect(events).toContainEqual({ action: `archive_${kind}_read`, kind: 'failure', count: 1, durationMs: expect.any(Number) });
  const commands = fetch.mock.calls.filter(call => call[1].method === 'POST' && call[0] !== '/me/activity-client-events');
  expect(new Headers(commands[0][1].headers).get('Idempotency-Key')).toBe(new Headers(commands[1][1].headers).get('Idempotency-Key'));
});
it.each(cases)('reports %s deletion conflict without treating it as success', async kind => {
  configureActivityTelemetry(true); const f = fixture(kind);
  const fetch = vi.fn((path: string, init: RequestInit) => Promise.resolve(path === '/me/activity-client-events'
    ? new Response(null, { status: 204 }) : init.method === 'DELETE' ? reply({ detail: 'Private error' }, 409) : reply(f.page)));
  f.mount(fetch); fireEvent.click(await screen.findByRole('button', { name: f.deleteName }));
  fireEvent.click(await screen.findByRole('checkbox', { name: 'I understand this cannot be undone.' }));
  fireEvent.click(screen.getByRole('button', { name: 'Confirm permanent deletion' }));
  await within(screen.getByRole('dialog')).findByText(kind === 'list' ? 'This deletion could not be applied. Check the archive and review the current List and card impact.'
    : 'This deletion could not be applied. Check the archive and review the current Card and parent List.');
  const events = await observations(fetch);
  expect(events).toContainEqual({ action: `archive_${kind}_delete`, kind: 'open', count: 1 });
  expect(events).toContainEqual({ action: `archive_${kind}_delete`, kind: 'conflict', count: 1 });
  expect(events).toContainEqual({ action: `archive_${kind}_delete`, kind: 'failure', count: 1, durationMs: expect.any(Number) });
  expect(events.some(event => event.action === `archive_${kind}_delete` && event.kind === 'success')).toBe(false);
  await act(async () => {});
});
it.each(cases)('retains %s reconnect observation through coalesced live invalidations', async kind => {
  configureActivityTelemetry(true); const f = fixture(kind); let reads = 0; let finish!: (value: Response) => void;
  const fetch = vi.fn((path: string) => {
    if (path === '/me/activity-client-events') return Promise.resolve(new Response(null, { status: 204 }));
    if (++reads === 2) return new Promise<Response>(resolve => { finish = resolve; });
    return Promise.resolve(reply(f.page));
  });
  f.mount(fetch); await screen.findByRole('button', { name: f.restoreName });
  await waitFor(() => expect(watchBoard).toHaveBeenCalled());
  const live = vi.mocked(watchBoard).mock.calls.at(-1)![0];
  act(() => { live.status('polling'); live.invalidate(); });
  await waitFor(() => expect(finish).toBeDefined());
  fireEvent(window, new Event('online'));
  act(() => { live.status('live'); live.invalidate(); live.invalidate(); });
  expect(reads).toBe(2);
  await act(async () => finish(reply(f.page)));
  await waitFor(() => expect(reads).toBe(3));
  const events = await observations(fetch);
  expect(events).toContainEqual({ action: `archive_${kind}_read`, kind: 'reconnect', count: 2 });
});
