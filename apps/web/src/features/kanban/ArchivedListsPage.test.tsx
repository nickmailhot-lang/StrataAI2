import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { ArchivedListsPage } from './ArchivedListsPage';
import { watchBoard } from '../../api/boardLive';
vi.mock('../../api/boardLive', () => ({ watchBoard: vi.fn(() => () => {}) }));
const org = '10000000-0000-4000-8000-000000000001'; const board = '10000000-0000-4000-8000-000000000002';
const list = { id: '20000000-0000-4000-8000-000000000001', organizationId: org, boardId: board, name: 'Planning',
  rank: '500000000000000000000000000000', version: 1, lifecycleState: 'archived' };
const row = { list, containedCardCount: 2 }; const page = { organizationId: org, boardId: board, items: [row], nextCursor: null };
const ack = { ...list, version: 2, lifecycleState: 'active' };
const reply = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
function mount(fetch: ReturnType<typeof vi.fn>) {
  vi.stubGlobal('fetch', fetch);
  const router = createMemoryRouter([{ path: '/app/:organizationId/boards/:boardId/archived-lists', element: <ArchivedListsPage /> }],
    { initialEntries: [`/app/${org}/boards/${board}/archived-lists`] });
  return render(<RouterProvider router={router} />);
}
async function review() { fireEvent.click(await screen.findByRole('button', { name: 'Restore Planning list' })); }
afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); vi.clearAllMocks(); });
it('shows counted archived Lists, submits a reviewed keyed restore and refreshes authoritative data', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(reply(page)).mockResolvedValueOnce(reply(ack))
    .mockResolvedValueOnce(reply({ ...page, items: [] })); mount(fetch);
  await review(); expect(screen.getByText('Restore Planning with its 2 contained cards?')).toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Confirm restore' }));
  await screen.findByText('No archived lists on this page.');
  expect(fetch.mock.calls[1][0]).toBe(`/lists/${list.id}/restore`);
  expect(JSON.parse(fetch.mock.calls[1][1].body)).toEqual({ version: 1 });
  expect(fetch.mock.calls[1][1].headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
  await waitFor(() => expect(screen.getByRole('button', { name: 'Check current archived lists' })).toHaveFocus());
});
it('recovers the same original intent even after canonical data no longer contains the restored List', async () => {
  const writes: RequestInit[] = []; let reads = 0;
  const fetch = vi.fn((_path: RequestInfo | URL, init?: RequestInit) => {
    if (init?.method === 'POST') { writes.push(init); return writes.length === 1 ? Promise.reject(new Error('Lost')) : Promise.resolve(reply(ack)); }
    return Promise.resolve(reply(reads++ === 0 ? page : { ...page, items: [] }));
  }); mount(fetch); await review(); fireEvent.click(screen.getByRole('button', { name: 'Confirm restore' }));
  const retry = await screen.findByRole('button', { name: 'Retry this restore' }); await waitFor(() => expect(retry).toBeEnabled());
  expect(screen.queryByRole('button', { name: 'Cancel restore' })).not.toBeInTheDocument();
  fireEvent.click(retry); await screen.findByText('No archived lists on this page.');
  expect(writes).toHaveLength(2); expect(writes[1].body).toBe(writes[0].body);
  expect(new Headers(writes[1].headers).get('Idempotency-Key')).toBe(new Headers(writes[0].headers).get('Idempotency-Key'));
});
it.each([{ ...ack, boardId: 'other' }, { ...ack, rank: 'other' }, { ...ack, version: 3 }])('rejects a mismatched successful acknowledgment: %j', async value => {
  mount(vi.fn().mockResolvedValueOnce(reply(page)).mockResolvedValueOnce(reply(value)).mockResolvedValue(reply(page)));
  await review(); fireEvent.click(screen.getByRole('button', { name: 'Confirm restore' }));
  await screen.findByRole('button', { name: 'Retry this restore' });
  expect(screen.queryByText('List restore acknowledged. Current archived Lists are being checked.')).not.toBeInTheDocument();
});
it('refreshes a selected review after a live revision without silently restoring stale consent', async () => {
  let invalidate: (() => void) | undefined;
  vi.mocked(watchBoard).mockImplementationOnce(options => { invalidate = options.invalidate; return () => {}; });
  const fetch = vi.fn().mockResolvedValueOnce(reply(page)).mockResolvedValue(reply({ ...page, items: [{ ...row, list: { ...list, version: 2 } }] }));
  mount(fetch); await review(); await waitFor(() => expect(invalidate).toBeDefined());
  await act(async () => { invalidate!(); });
  await screen.findByText('This List changed. Cancel this review and check the current archive before another restore.');
  expect(screen.getByRole('button', { name: 'Confirm restore' })).toBeDisabled();
  expect(fetch.mock.calls.every(([, init]) => init.method !== 'POST')).toBe(true);
});
it.each([{ ...page, organizationId: 'other' }, { ...page, items: [{ ...row, containedCardCount: -1 }] },
  { ...page, items: [row, row] }, { ...page, nextCursor: list.id }])('fails closed on invalid archive data: %j', async value => {
  mount(vi.fn().mockResolvedValue(reply(value)));
  await screen.findByText('Unable to confirm current archived Lists. Please check again.');
  expect(screen.queryByRole('button', { name: 'Restore Planning list' })).not.toBeInTheDocument();
});
it('clears a review after denied persistence and displays no private response title', async () => {
  mount(vi.fn().mockResolvedValueOnce(reply(page)).mockResolvedValueOnce(reply({ title: 'private SQL', code: 'list_not_found' }, 404))
    .mockResolvedValue(reply({}, 404)));
  await review(); fireEvent.click(screen.getByRole('button', { name: 'Confirm restore' }));
  await screen.findByText('Archived List administration is unavailable.');
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument(); expect(screen.queryByText('private SQL')).not.toBeInTheDocument();
});
it('aborts a pending scoped read on unmount', async () => {
  const fetch = vi.fn((_path: RequestInfo | URL, _init?: RequestInit) => new Promise<Response>(() => {}));
  const view = mount(fetch); await waitFor(() => expect(fetch).toHaveBeenCalled()); view.unmount();
  expect(fetch.mock.calls[0][1]?.signal?.aborted).toBe(true);
});
it('retains the requested next page through read failure and explicit recovery', async () => {
  const items = Array.from({ length: 50 }, (_, i) => ({ ...row, list: { ...list,
    id: `20000000-0000-4000-8000-${String(i + 1).padStart(12, '0')}`, name: `Archived ${i + 1}` } }));
  const cursor = items[49].list.id;
  const nextRow = { ...row, list: { ...list, id: '20000000-0000-4000-8000-000000000051', name: 'Next page' } };
  const fetch = vi.fn().mockResolvedValueOnce(reply({ ...page, items, nextCursor: cursor }))
    .mockResolvedValueOnce(reply({}, 503)).mockResolvedValueOnce(reply({ ...page, items: [nextRow] }));
  mount(fetch); await screen.findByRole('heading', { name: 'Archived 1' });
  fireEvent.click(screen.getByRole('button', { name: 'Next archived lists' }));
  await screen.findByText('Unable to confirm current archived Lists. Please check again.');
  expect(screen.queryByRole('heading', { name: 'Archived 1' })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Check current archived lists' }));
  await screen.findByRole('heading', { name: 'Next page' });
  expect(fetch.mock.calls[2][0]).toBe(`/boards/${board}/archived-lists?after=${cursor}`);
  expect(screen.getByRole('button', { name: 'Previous archived lists' })).toBeEnabled();
});
it('bounds a stalled restore and keeps its retry available after fresh archive admission', async () => {
  const fetch = vi.fn((_path: RequestInfo | URL, init?: RequestInit) => init?.method === 'POST'
    ? new Promise<Response>(() => {}) : Promise.resolve(reply(page)));
  mount(fetch); await review(); vi.useFakeTimers();
  fireEvent.click(screen.getByRole('button', { name: 'Confirm restore' }));
  await act(async () => { await vi.advanceTimersByTimeAsync(15_000); });
  expect(screen.getByRole('button', { name: 'Retry this restore' })).toBeEnabled();
  expect(fetch.mock.calls.find(([, init]) => init?.method === 'POST')?.[1]?.signal?.aborted).toBe(true);
});
it('allows an unresolved restore to recheck authority inside its dialog after a failed canonical read', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(reply(page)).mockRejectedValueOnce(new Error('Lost'))
    .mockResolvedValueOnce(reply({}, 503)).mockResolvedValueOnce(reply({ ...page, items: [] }))
    .mockResolvedValueOnce(reply(ack)).mockResolvedValue(reply({ ...page, items: [] }));
  mount(fetch); await review(); fireEvent.click(screen.getByRole('button', { name: 'Confirm restore' }));
  const retry = await screen.findByRole('button', { name: 'Retry this restore' });
  await within(await screen.findByRole('dialog')).findByText('Unable to confirm current archived Lists. Please check again.'); expect(retry).toBeDisabled();
  fireEvent.click(screen.getByRole('button', { name: 'Check current archive for this restore' }));
  await waitFor(() => expect(retry).toBeEnabled()); fireEvent.click(retry);
  await screen.findByText('No archived lists on this page.');
  expect(fetch.mock.calls[4][1].body).toBe(fetch.mock.calls[1][1].body);
  expect(fetch.mock.calls[4][1].headers.get('Idempotency-Key')).toBe(fetch.mock.calls[1][1].headers.get('Idempotency-Key'));
});
async function reviewDeletion() { fireEvent.click(await screen.findByRole('button', { name: 'Permanently delete Planning list' })); }
function confirmDeletion() { fireEvent.click(screen.getByRole('checkbox', { name: 'I understand this cannot be undone.' })); }
it.each([0, 2])('requires explicit irreversible consent and sends the reviewed %i-card impact', async count => {
  const fetch = vi.fn().mockResolvedValueOnce(reply({ ...page, items: [{ ...row, containedCardCount: count }] }))
    .mockResolvedValueOnce(reply({ ...ack, lifecycleState: 'deleted' })).mockResolvedValue(reply({ ...page, items: [] }));
  mount(fetch); await reviewDeletion();
  expect(screen.getByText(`Permanently delete Planning and make its ${count} contained cards unavailable?`)).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Confirm permanent deletion' })).toBeDisabled();
  confirmDeletion(); fireEvent.click(screen.getByRole('button', { name: 'Confirm permanent deletion' }));
  await screen.findByText('No archived lists on this page.');
  expect(fetch.mock.calls[1][0]).toBe(`/lists/${list.id}?version=1&confirmed=true&containedCardCount=${count}`);
  expect(fetch.mock.calls[1][1].method).toBe('DELETE'); expect(fetch.mock.calls[1][1].body).toBeUndefined();
  expect(fetch.mock.calls[1][1].headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
});
it('keeps deletion count/version/confirmation/key unchanged after loss and canonical removal', async () => {
  const writes: { path: RequestInfo | URL; init: RequestInit }[] = []; let reads = 0;
  const fetch = vi.fn((path: RequestInfo | URL, init?: RequestInit) => {
    if (init?.method === 'DELETE') { writes.push({ path, init }); return writes.length === 1 ? Promise.reject(new Error('Lost'))
      : Promise.resolve(reply({ ...ack, lifecycleState: 'deleted' })); }
    return Promise.resolve(reply(reads++ === 0 ? page : { ...page, items: [] }));
  }); mount(fetch); await reviewDeletion(); confirmDeletion(); fireEvent.click(screen.getByRole('button', { name: 'Confirm permanent deletion' }));
  const retry = await screen.findByRole('button', { name: 'Retry this deletion' }); await waitFor(() => expect(retry).toBeEnabled());
  expect(screen.getByRole('checkbox', { name: 'I understand this cannot be undone.' })).toBeDisabled();
  expect(screen.queryByRole('button', { name: 'Cancel deletion' })).not.toBeInTheDocument();
  fireEvent.click(retry); await screen.findByText('No archived lists on this page.');
  expect(writes).toHaveLength(2); expect(writes[1].path).toBe(writes[0].path);
  expect(new Headers(writes[1].init.headers).get('Idempotency-Key')).toBe(new Headers(writes[0].init.headers).get('Idempotency-Key'));
});
it('invalidates consent when card impact changes without a List version change', async () => {
  let invalidate: (() => void) | undefined;
  vi.mocked(watchBoard).mockImplementationOnce(options => { invalidate = options.invalidate; return () => {}; });
  const fetch = vi.fn().mockResolvedValueOnce(reply(page)).mockResolvedValue(reply({ ...page, items: [{ ...row, containedCardCount: 3 }] }));
  mount(fetch); await reviewDeletion(); confirmDeletion(); await waitFor(() => expect(invalidate).toBeDefined());
  await act(async () => { invalidate!(); });
  await screen.findByText('This List or card impact changed. Cancel this review and check the current archive before deletion.');
  expect(screen.getByRole('button', { name: 'Confirm permanent deletion' })).toBeDisabled();
  fireEvent.click(screen.getByRole('button', { name: 'Cancel deletion' }));
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  await reviewDeletion(); expect(screen.getByRole('checkbox', { name: 'I understand this cannot be undone.' })).not.toBeChecked();
  expect(screen.getByText('Permanently delete Planning and make its 3 contained cards unavailable?')).toBeInTheDocument();
  expect(fetch.mock.calls.every(([, init]) => init.method !== 'DELETE')).toBe(true);
});
it('rejects a restore-shaped acknowledgment for deletion and retains the delete intent', async () => {
  mount(vi.fn().mockResolvedValueOnce(reply(page)).mockResolvedValueOnce(reply(ack)).mockResolvedValue(reply(page)));
  await reviewDeletion(); confirmDeletion(); fireEvent.click(screen.getByRole('button', { name: 'Confirm permanent deletion' }));
  await screen.findByRole('button', { name: 'Retry this deletion' });
  expect(screen.queryByText('List deletion acknowledged. Current archived Lists are being checked.')).not.toBeInTheDocument();
});
it('requires fresh review after a rejected impact and displays no server detail', async () => {
  mount(vi.fn().mockResolvedValueOnce(reply(page)).mockResolvedValueOnce(reply({ code: 'deletion_impact_changed', title: 'private SQL' }, 409))
    .mockResolvedValue(reply(page)));
  await reviewDeletion(); confirmDeletion(); fireEvent.click(screen.getByRole('button', { name: 'Confirm permanent deletion' }));
  await within(await screen.findByRole('dialog')).findByText('This deletion could not be applied. Check the archive and review the current List and card impact.');
  expect(screen.getByRole('button', { name: 'Confirm permanent deletion' })).toBeDisabled();
  expect(screen.queryByText('private SQL')).not.toBeInTheDocument();
});
