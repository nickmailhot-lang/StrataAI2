import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { ArchivedCardsPage } from './ArchivedCardsPage';
import { watchBoard } from '../../api/boardLive';
vi.mock('../../api/boardLive', () => ({ watchBoard: vi.fn(() => () => {}) }));
const org = '10000000-0000-4000-8000-000000000001'; const board = '10000000-0000-4000-8000-000000000002';
const list = { id: '20000000-0000-4000-8000-000000000001', organizationId: org, boardId: board, name: 'Planning', rank: '500', version: 1, lifecycleState: 'active' };
const card = { id: '30000000-0000-4000-8000-000000000001', organizationId: org, boardId: board, listId: list.id,
  title: 'Review budget', description: null, rank: '500', version: 2, lifecycleState: 'archived' };
const row = { card, list }; const page = { organizationId: org, boardId: board, items: [row], nextCursor: null };
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
