import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { ArchivedCardsPage } from './ArchivedCardsPage';
import { watchBoard } from '../../api/boardLive';
vi.mock('../../api/boardLive', () => ({ watchBoard: vi.fn(() => () => {}) }));
const org = '10000000-0000-4000-8000-000000000001'; const board = '10000000-0000-4000-8000-000000000002';
const list = { id: '20000000-0000-4000-8000-000000000001', organizationId: org, boardId: board, name: 'Planning', rank: '500', version: 1, lifecycleState: 'active' };
const card = { id: '30000000-0000-4000-8000-000000000001', organizationId: org, boardId: board, listId: list.id,
  title: 'Review budget', description: null, rank: '500', version: 2, lifecycleState: 'archived' };
const row = { card, list }; const page = { organizationId: org, boardId: board, items: [row], nextCursor: null, canDelete: true };
const ack = { ...card, version: 3, lifecycleState: 'active', description: 'Detail stays out of archive UI' };
const reply = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
function mount(fetch: ReturnType<typeof vi.fn>) {
  vi.stubGlobal('fetch', fetch);
  const router = createMemoryRouter([{ path: '/app/:organizationId/boards/:boardId/archived-cards', element: <ArchivedCardsPage /> }],
    { initialEntries: [`/app/${org}/boards/${board}/archived-cards`] });
  return { ...render(<RouterProvider router={router} />), router };
}
async function review() { fireEvent.click(await screen.findByRole('button', { name: 'Restore Review budget card' })); }
afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); vi.clearAllMocks(); });
it('reviews a Card and current parent, sends only the version/key and restores focus after current discovery', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(reply(page)).mockResolvedValueOnce(reply(ack)).mockResolvedValue(reply({ ...page, items: [] })); mount(fetch);
  await review(); expect(screen.getByText('Restore Review budget to Planning?')).toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Confirm restore' })); await screen.findByText('No archived cards on this page.');
  expect(fetch.mock.calls[1][0]).toBe(`/cards/${card.id}/restore`); expect(JSON.parse(fetch.mock.calls[1][1].body)).toEqual({ version: 2 });
  expect(fetch.mock.calls[1][1].headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
  expect(screen.queryByText(ack.description)).not.toBeInTheDocument();
  await waitFor(() => expect(screen.getByRole('button', { name: 'Check current archived cards' })).toHaveFocus());
});
it('preserves archive focus when a following realtime read disables the return target', async () => {
  let invalidate!: () => void; let resolve!: (value: Response) => void;
  vi.mocked(watchBoard).mockImplementationOnce(options => { invalidate = options.invalidate; return () => {}; });
  mount(vi.fn().mockResolvedValueOnce(reply(page)).mockResolvedValueOnce(reply({ ...ack, lifecycleState: 'deleted' }))
    .mockResolvedValueOnce(reply({ ...page, items: [] })).mockReturnValueOnce(new Promise<Response>(done => { resolve = done; })));
  await reviewDeletion(); confirmDeletion();
  const refresh = await screen.findByRole('button', { name: 'Check current archived cards' });
  await waitFor(() => expect(refresh).toHaveFocus());
  await act(async () => invalidate()); expect(refresh).toBeDisabled();
  // Native browsers drop focus when a focused button becomes disabled.
  refresh.blur(); expect(refresh).not.toHaveFocus();
  await act(async () => resolve(reply({ ...page, items: [] })));
  await waitFor(() => expect(refresh).toBeEnabled()); await waitFor(() => expect(refresh).toHaveFocus());
});
it('retains the exact historical restore after the committed Card disappears from discovery', async () => {
  const writes: RequestInit[] = []; let reads = 0;
  const fetch = vi.fn((_path: RequestInfo | URL, init?: RequestInit) => {
    if (init?.method === 'POST') { writes.push(init); return writes.length === 1 ? Promise.reject(new Error('Lost')) : Promise.resolve(reply(ack)); }
    return Promise.resolve(reply(reads++ === 0 ? page : { ...page, items: [] }));
  }); mount(fetch); await review(); fireEvent.click(screen.getByRole('button', { name: 'Confirm restore' }));
  const retry = await screen.findByRole('button', { name: 'Retry this restore' }); await waitFor(() => expect(retry).toBeEnabled());
  expect(screen.queryByRole('button', { name: 'Cancel restore' })).not.toBeInTheDocument();
  fireEvent.click(retry); await screen.findByText('No archived cards on this page.');
  expect(writes).toHaveLength(2); expect(writes[1].body).toBe(writes[0].body);
  expect(new Headers(writes[1].headers).get('Idempotency-Key')).toBe(new Headers(writes[0].headers).get('Idempotency-Key'));
});
it('shows archived parent context and prevents Card restoration before List restoration', async () => {
  mount(vi.fn().mockResolvedValue(reply({ ...page, items: [{ ...row, list: { ...list, lifecycleState: 'archived' } }] })));
  expect(await screen.findByRole('button', { name: 'Restore Review budget card' })).toBeDisabled();
  expect(screen.getByText('Restore the parent List before restoring this Card.')).toBeInTheDocument();
});
it('invalidates reviewed parent authority on a live List change without changing Card version', async () => {
  let invalidate!: () => void; vi.mocked(watchBoard).mockImplementationOnce(options => { invalidate = options.invalidate; return () => {}; });
  mount(vi.fn().mockResolvedValueOnce(reply(page)).mockResolvedValue(reply({ ...page, items: [{ ...row, list: { ...list, version: 2, lifecycleState: 'archived' } }] })));
  await review(); await act(async () => { invalidate(); });
  await screen.findByText('This Card or its parent List changed. Cancel this review and check the current archive before another restore.');
  expect(screen.getByRole('button', { name: 'Confirm restore' })).toBeDisabled();
});
it.each([{ ...ack, listId: 'other' }, { ...ack, boardId: 'other' }, { ...ack, rank: 'other' }, { ...ack, version: 4 }, { ...ack, lifecycleState: 'archived' }])('retains recovery for mismatched acknowledgment %j', async value => {
  mount(vi.fn().mockResolvedValueOnce(reply(page)).mockResolvedValueOnce(reply(value)).mockResolvedValue(reply(page)));
  await review(); fireEvent.click(screen.getByRole('button', { name: 'Confirm restore' })); await screen.findByRole('button', { name: 'Retry this restore' });
  expect(screen.queryByText('Card restore acknowledged. Current archived Cards are being checked.')).not.toBeInTheDocument();
});
it.each([{ ...page, organizationId: 'other' }, { ...page, items: [row, row] }, { ...page, nextCursor: card.id },
  { ...page, canDelete: undefined }, { ...page, canDelete: 'true' },
  { ...page, items: [{ ...row, card: { ...card, description: 'Private detail' } }] },
  { ...page, items: [{ ...row, list: { ...list, lifecycleState: 'deleted' } }] }])('fails closed on invalid discovery %j', async value => {
  mount(vi.fn().mockResolvedValue(reply(value))); await screen.findByText('Unable to confirm current archived Cards. Please check again.');
  expect(screen.queryByRole('button', { name: 'Restore Review budget card' })).not.toBeInTheDocument();
  expect(screen.queryByText('Private detail')).not.toBeInTheDocument();
});
it('clears reviewed protected data on a denied read and ignores the interrupted late write', async () => {
  let invalidate!: () => void; let resolve!: (response: Response) => void; let reads = 0;
  vi.mocked(watchBoard).mockImplementationOnce(options => { invalidate = options.invalidate; return () => {}; });
  const fetch = vi.fn((_path: RequestInfo | URL, init?: RequestInit) => {
    if (init?.method === 'POST') return new Promise<Response>(done => { resolve = done; });
    return Promise.resolve(reads++ === 0 ? reply(page) : reply({ title: 'Private error detail' }, 404));
  }); mount(fetch); await review(); fireEvent.click(screen.getByRole('button', { name: 'Confirm restore' }));
  await act(async () => { invalidate(); }); await screen.findByText('Archived Card access is unavailable.');
  await act(async () => { resolve(reply(ack)); });
  expect(screen.queryByText('Review budget')).not.toBeInTheDocument(); expect(screen.queryByText('Private error detail')).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Retry this restore' })).not.toBeInTheDocument();
  expect(screen.queryByText('Card restore acknowledged. Current archived Cards are being checked.')).not.toBeInTheDocument();
});
it('queues live invalidations behind a read instead of repeatedly aborting a slow current page', async () => {
  let invalidate!: () => void; let resolve!: (response: Response) => void;
  vi.mocked(watchBoard).mockImplementationOnce(options => { invalidate = options.invalidate; return () => {}; });
  const fetch = vi.fn().mockResolvedValueOnce(reply(page)).mockImplementationOnce(() => new Promise<Response>(done => { resolve = done; }))
    .mockResolvedValue(reply({ ...page, items: [] })); mount(fetch); await screen.findByRole('button', { name: 'Restore Review budget card' });
  await waitFor(() => expect(invalidate).toBeTypeOf('function'));
  await act(async () => { invalidate(); invalidate(); invalidate(); }); expect(fetch).toHaveBeenCalledTimes(2);
  expect(fetch.mock.calls[1][1].signal.aborted).toBe(false);
  await act(async () => { resolve(reply(page)); }); await screen.findByText('No archived cards on this page.'); expect(fetch).toHaveBeenCalledTimes(3);
});
it('seeks through a full page and returns to the same previous cursor', async () => {
  const rows = Array.from({ length: 50 }, (_, i) => ({ ...row, card: { ...card,
    id: `30000000-0000-4000-8000-${(i + 1).toString(16).padStart(12, '0')}`, title: `Card ${i + 1}` } }));
  const cursor = rows.at(-1)!.card.id;
  const first = { ...page, items: rows, nextCursor: cursor };
  const fetch = vi.fn().mockResolvedValueOnce(reply(first)).mockResolvedValueOnce(reply({ ...page,
    items: [{ ...row, card: { ...card, id: '30000000-0000-4000-8000-000000000033' } }] })).mockResolvedValue(reply(first)); mount(fetch);
  fireEvent.click(await screen.findByRole('button', { name: 'Next archived cards' }));
  await screen.findByRole('button', { name: 'Restore Review budget card' });
  expect(fetch.mock.calls[1][0]).toBe(`/boards/${board}/archived-cards?after=${cursor}`);
  expect(screen.getByRole('button', { name: 'Next archived cards' })).toBeDisabled();
  fireEvent.click(screen.getByRole('button', { name: 'Previous archived cards' })); await screen.findByRole('button', { name: 'Restore Card 1 card' });
  expect(fetch.mock.calls[2][0]).toBe(`/boards/${board}/archived-cards`);
});
it('bounds a hung acknowledgment body and preserves the same request after its deadline', async () => {
  let signal!: AbortSignal; let count = 0;
  const fetch = vi.fn((_path: RequestInfo | URL, init?: RequestInit) => {
    if (init?.method === 'POST') { signal = init.signal!; count++;
      return Promise.resolve(count === 1 ? { status: 200, json: () => new Promise(() => {}) } : reply(ack)); }
    return Promise.resolve(reply(page));
  }); mount(fetch); await review(); vi.useFakeTimers(); fireEvent.click(screen.getByRole('button', { name: 'Confirm restore' }));
  await act(async () => { await vi.advanceTimersByTimeAsync(15_001); }); expect(signal.aborted).toBe(true);
  expect(screen.getByRole('button', { name: 'Retry this restore' })).toBeEnabled();
  fireEvent.click(screen.getByRole('button', { name: 'Retry this restore' })); await act(async () => {});
  const writes = fetch.mock.calls.map(([, init]) => init).filter((init): init is RequestInit => init?.method === 'POST'); expect(writes).toHaveLength(2);
  expect(writes[1].body).toBe(writes[0].body);
  expect(new Headers(writes[1].headers).get('Idempotency-Key')).toBe(new Headers(writes[0].headers).get('Idempotency-Key'));
});
async function reviewDeletion() { fireEvent.click(await screen.findByRole('button', { name: 'Permanently delete Review budget card' })); }
it('defers deletion review during live admission and requires fresh unchecked consent', async () => {
  let invalidate!: () => void; let resolve!: (value: Response) => void;
  vi.mocked(watchBoard).mockImplementationOnce(options => { invalidate = options.invalidate; return () => {}; });
  const fetch = vi.fn().mockResolvedValueOnce(reply(page)).mockReturnValueOnce(new Promise<Response>(done => { resolve = done; })); mount(fetch);
  const trigger = await screen.findByRole('button', { name: 'Permanently delete Review budget card' });
  await act(async () => invalidate()); expect(trigger).toBeEnabled(); trigger.focus(); expect(trigger).toHaveFocus(); fireEvent.click(trigger);
  expect(screen.getByRole('dialog')).toHaveTextContent('Checking current Card and deletion access…');
  expect(screen.queryByRole('checkbox')).not.toBeInTheDocument(); expect(fetch).toHaveBeenCalledTimes(2);
  await act(async () => resolve(reply({ ...page, items: [{ ...row, card: { ...card, title: 'Current budget', version: 3 } }] })));
  expect(await screen.findByText('Permanently delete Current budget from Planning?')).toBeVisible();
  expect(screen.getByRole('checkbox', { name: 'I understand this cannot be undone.' })).not.toBeChecked();
  expect(screen.getByRole('button', { name: 'Confirm permanent deletion' })).toBeDisabled();
  expect(fetch.mock.calls.every(call => !call[1].method || call[1].method === 'GET')).toBe(true);
});
it.each(['closed', 'denied', 'removed'])('does not revive deferred deletion review after %s', async outcome => {
  let invalidate!: () => void; let resolve!: (value: Response) => void;
  vi.mocked(watchBoard).mockImplementationOnce(options => { invalidate = options.invalidate; return () => {}; });
  const fetch = vi.fn().mockResolvedValueOnce(reply(page)).mockReturnValueOnce(new Promise<Response>(done => { resolve = done; })); mount(fetch);
  await screen.findByRole('button', { name: 'Permanently delete Review budget card' });
  await act(async () => invalidate()); await reviewDeletion();
  if (outcome === 'closed') fireEvent.click(screen.getByRole('button', { name: 'Cancel deletion review' }));
  await act(async () => resolve(reply(outcome === 'denied' ? { ...page, canDelete: false } : outcome === 'removed' ? { ...page, items: [] } : page)));
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  expect(screen.queryByRole('button', { name: 'Confirm permanent deletion' })).not.toBeInTheDocument();
  expect(fetch).toHaveBeenCalledTimes(2);
});
function confirmDeletion() {
  fireEvent.click(screen.getByRole('checkbox', { name: 'I understand this cannot be undone.' }));
  fireEvent.click(screen.getByRole('button', { name: 'Confirm permanent deletion' }));
}
it('requires explicit irreversible consent and sends the reviewed deletion without a request body', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(reply(page)).mockResolvedValueOnce(reply({ ...ack, lifecycleState: 'deleted' }))
    .mockResolvedValue(reply({ ...page, items: [] })); mount(fetch); await reviewDeletion();
  expect(screen.getByText('Permanently delete Review budget from Planning?')).toBeInTheDocument();
  expect(screen.getByText('This cannot be undone. This Card can no longer be restored or used.')).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Confirm permanent deletion' })).toBeDisabled(); expect(fetch).toHaveBeenCalledOnce();
  confirmDeletion(); await screen.findByText('No archived cards on this page.');
  expect(fetch.mock.calls[1][0]).toBe(`/cards/${card.id}?version=2&confirmed=true`); expect(fetch.mock.calls[1][1].method).toBe('DELETE');
  expect(fetch.mock.calls[1][1].body).toBeUndefined(); expect(fetch.mock.calls[1][1].headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
  await waitFor(() => expect(screen.getByRole('button', { name: 'Check current archived cards' })).toHaveFocus());
});
it('lets contributors restore but hides permanent deletion when current capability is false', async () => {
  mount(vi.fn().mockResolvedValue(reply({ ...page, canDelete: false })));
  expect(await screen.findByRole('button', { name: 'Restore Review budget card' })).toBeEnabled();
  expect(screen.queryByRole('button', { name: 'Permanently delete Review budget card' })).not.toBeInTheDocument();
});
it('allows reviewed administrative deletion under an archived nondeleted parent', async () => {
  mount(vi.fn().mockResolvedValue(reply({ ...page, items: [{ ...row, list: { ...list, lifecycleState: 'archived' } }] })));
  await reviewDeletion(); fireEvent.click(screen.getByRole('checkbox', { name: 'I understand this cannot be undone.' }));
  expect(screen.getByRole('button', { name: 'Confirm permanent deletion' })).toBeEnabled();
});
it('preserves original confirmation/version/key after a lost committed deletion removes its row', async () => {
  const writes: { path: string; init: RequestInit }[] = []; let reads = 0;
  const fetch = vi.fn((path: string, init: RequestInit) => {
    if (init.method === 'DELETE') { writes.push({ path, init }); return writes.length === 1 ? Promise.reject(new Error('Lost')) : Promise.resolve(reply({ ...ack, lifecycleState: 'deleted' })); }
    return Promise.resolve(reply(reads++ === 0 ? page : { ...page, items: [] }));
  }); mount(fetch); await reviewDeletion(); confirmDeletion();
  const retry = await screen.findByRole('button', { name: 'Retry this deletion' }); await waitFor(() => expect(retry).toBeEnabled());
  expect(screen.getByRole('checkbox', { name: 'I understand this cannot be undone.' })).toBeDisabled();
  expect(screen.queryByRole('button', { name: 'Cancel deletion' })).not.toBeInTheDocument();
  fireEvent.click(retry); await screen.findByText('No archived cards on this page.');
  expect(writes).toHaveLength(2); expect(writes[1].path).toBe(writes[0].path); expect(writes[1].init.body).toBeUndefined();
  expect(new Headers(writes[1].init.headers).get('Idempotency-Key')).toBe(new Headers(writes[0].init.headers).get('Idempotency-Key'));
});
it('invalidates deletion consent on a live revision and resets it on fresh review', async () => {
  let invalidate!: () => void; vi.mocked(watchBoard).mockImplementationOnce(options => { invalidate = options.invalidate; return () => {}; });
  mount(vi.fn().mockResolvedValueOnce(reply(page)).mockResolvedValue(reply({ ...page, items: [{ ...row, card: { ...card, version: 3 } }] })));
  await reviewDeletion(); fireEvent.click(screen.getByRole('checkbox', { name: 'I understand this cannot be undone.' }));
  await act(async () => { invalidate(); });
  await screen.findByText('This Card or its parent List changed. Cancel this review and check the current archive before deletion.');
  expect(screen.getByRole('button', { name: 'Confirm permanent deletion' })).toBeDisabled();
  fireEvent.click(screen.getByRole('button', { name: 'Cancel deletion' })); await reviewDeletion();
  expect(screen.getByRole('checkbox', { name: 'I understand this cannot be undone.' })).not.toBeChecked();
});
it('aborts and fences a pending deletion when editing remains allowed but administration is revoked', async () => {
  let invalidate!: () => void; let resolve!: (response: Response) => void; let signal!: AbortSignal; let reads = 0;
  vi.mocked(watchBoard).mockImplementationOnce(options => { invalidate = options.invalidate; return () => {}; });
  mount(vi.fn((_path: string, init: RequestInit) => {
    if (init.method === 'DELETE') { signal = init.signal!; return new Promise<Response>(done => { resolve = done; }); }
    return Promise.resolve(reply(reads++ === 0 ? page : { ...page, canDelete: false }));
  })); await reviewDeletion(); confirmDeletion(); await act(async () => { invalidate(); });
  await screen.findByText('Permanent Card deletion is unavailable.'); expect(signal.aborted).toBe(true);
  await act(async () => { resolve(reply({ ...ack, lifecycleState: 'deleted' })); });
  expect(screen.queryByRole('button', { name: 'Retry this deletion' })).not.toBeInTheDocument();
  expect(screen.queryByText('Card deletion acknowledged. Current archived Cards are being checked.')).not.toBeInTheDocument();
  expect(await screen.findByRole('button', { name: 'Restore Review budget card' })).toBeEnabled();
});
it('keeps a completed acknowledgment when the following page has only contributor capability', async () => {
  mount(vi.fn().mockResolvedValueOnce(reply(page)).mockResolvedValueOnce(reply({ ...ack, lifecycleState: 'deleted' }))
    .mockResolvedValue(reply({ ...page, canDelete: false, items: [] })));
  await reviewDeletion(); confirmDeletion(); await screen.findByText('Card deletion acknowledged. Current archived Cards are being checked.');
  expect(screen.queryByText('Permanent Card deletion is unavailable.')).not.toBeInTheDocument();
});
it('rejects restore-shaped deletion acknowledgment and preserves the delete intent', async () => {
  mount(vi.fn().mockResolvedValueOnce(reply(page)).mockResolvedValueOnce(reply(ack)).mockResolvedValue(reply(page)));
  await reviewDeletion(); confirmDeletion(); await screen.findByRole('button', { name: 'Retry this deletion' });
  expect(screen.queryByText('Card deletion acknowledged. Current archived Cards are being checked.')).not.toBeInTheDocument();
});
