import { act, fireEvent, render, screen } from '@testing-library/react';
import { CardArchiveControl } from './CardArchiveControl';
import type { BoardSnapshot } from '../../api/workManagement';
const card = { id: 'card', title: 'Review budget', description: 'Private detail', rank: '500', version: 1 };
const list = { id: 'list', name: 'Planning', rank: '500', version: 1, lifecycleState: 'active' };
const snapshot: BoardSnapshot = { board: { id: 'board', organizationId: 'org', name: 'Board', description: null, lifecycleState: 'active' },
  access: { canView: true, canMove: true, canEdit: true, canAdminister: false }, lists: [{ list, cards: [card] }] };
const props = { cardId: card.id, card, snapshot, disabled: false, onBusyChange: vi.fn(), onRecoveryChange: vi.fn(), onRefresh: vi.fn(), onAcknowledged: vi.fn() };
const ack = { ...card, organizationId: 'org', boardId: 'board', listId: 'list', lifecycleState: 'archived', version: 2 };
const reply = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
function review() { fireEvent.click(screen.getByRole('button', { name: 'Archive Card' })); }
function archive() { fireEvent.click(screen.getByRole('button', { name: 'Confirm Card archive' })); }
afterEach(() => { vi.clearAllMocks(); vi.unstubAllGlobals(); vi.useRealTimers(); });
it('reviews reversible archival and sends only the original version/key', async () => {
  const fetch = vi.fn().mockResolvedValue(reply(ack)); vi.stubGlobal('fetch', fetch); render(<CardArchiveControl {...props} />);
  review(); expect(screen.getByText('Archive Review budget from Planning?')).toBeInTheDocument();
  expect(screen.getByText('This hides the Card from the Board. It can be restored from Archived cards when its parent List is active.')).toBeInTheDocument(); archive();
  await screen.findByText('Card archive acknowledged. Current Board state is being checked.');
  expect(fetch.mock.calls[0][0]).toBe('/cards/card/archive'); expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ version: 1 });
  expect(fetch.mock.calls[0][1].headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/); expect(props.onAcknowledged).toHaveBeenCalledOnce();
});
it('recovers the unchanged intent after canonical Card removal and newer parent revision', async () => {
  const fetch = vi.fn().mockRejectedValueOnce(new Error('Lost')).mockResolvedValueOnce(reply(ack)); vi.stubGlobal('fetch', fetch);
  const view = render(<CardArchiveControl {...props} />); review(); archive(); await screen.findByRole('button', { name: 'Retry this Card archive' });
  view.rerender(<CardArchiveControl {...props} card={undefined} snapshot={{ ...snapshot, lists: [{ list: { ...list, version: 3 }, cards: [] }] }} />);
  expect(screen.queryByRole('button', { name: 'Cancel Card archive' })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Retry this Card archive' })); await screen.findByText('Card archive acknowledged. Current Board state is being checked.');
  expect(fetch.mock.calls[1][1].body).toBe(fetch.mock.calls[0][1].body);
  expect(fetch.mock.calls[1][1].headers.get('Idempotency-Key')).toBe(fetch.mock.calls[0][1].headers.get('Idempotency-Key'));
});
it.each([{ ...ack, id: 'other' }, { ...ack, organizationId: 'other' }, { ...ack, boardId: 'other' }, { ...ack, listId: 'other' }, { ...ack, title: 'other' },
  { ...ack, rank: 'other' }, { ...ack, version: 3 }, { ...ack, lifecycleState: 'active' }])('rejects mismatched acknowledgment %j', async value => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(reply(value))); render(<CardArchiveControl {...props} />); review(); archive();
  await screen.findByRole('button', { name: 'Retry this Card archive' }); expect(props.onAcknowledged).not.toHaveBeenCalled();
});
it('invalidates an unsubmitted review when its current parent changes', () => {
  const view = render(<CardArchiveControl {...props} />); review();
  view.rerender(<CardArchiveControl {...props} snapshot={{ ...snapshot, lists: [{ list: { ...list, version: 2 }, cards: [card] }] }} />);
  expect(screen.getByRole('button', { name: 'Confirm Card archive' })).toBeDisabled();
  fireEvent.click(screen.getByRole('button', { name: 'Cancel Card archive' })); expect(screen.getByRole('button', { name: 'Archive Card' })).toHaveFocus();
});
it('requires fresh review after a conflict without rendering the server error title', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(reply({ title: 'Private SQL error' }, 409))); render(<CardArchiveControl {...props} />); review(); archive();
  await screen.findByText('This archive could not be applied. Check the Board and review the current Card.');
  expect(screen.getByRole('button', { name: 'Confirm Card archive' })).toBeDisabled(); expect(screen.queryByText('Private SQL error')).not.toBeInTheDocument();
});
it('clears a denied review and hides its archive action', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(reply({ title: 'Private SQL error' }, 404))); render(<CardArchiveControl {...props} />); review(); archive();
  await screen.findByText('This Card or archive action is unavailable.'); expect(screen.queryByText('Archive Review budget from Planning?')).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Archive Card' })).not.toBeInTheDocument();
});
it('aborts pending work and ignores its late acknowledgment after editing is revoked', async () => {
  let resolve!: (response: Response) => void; let signal!: AbortSignal;
  vi.stubGlobal('fetch', vi.fn((_path: string, init: RequestInit) => { signal = init.signal!; return new Promise<Response>(done => { resolve = done; }); }));
  const view = render(<CardArchiveControl {...props} />); review(); archive();
  view.rerender(<CardArchiveControl {...props} snapshot={{ ...snapshot, access: { ...snapshot.access, canEdit: false } }} />);
  expect(signal.aborted).toBe(true); await act(async () => { resolve(reply(ack)); }); expect(props.onAcknowledged).not.toHaveBeenCalled();
  expect(screen.queryByRole('button', { name: 'Retry this Card archive' })).not.toBeInTheDocument();
});
it('bounds an unresponsive acknowledgment body and retains receipt recovery', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ status: 200, json: () => new Promise(() => {}) }));
  render(<CardArchiveControl {...props} />); review(); vi.useFakeTimers(); archive(); await act(async () => { await vi.advanceTimersByTimeAsync(15_001); });
  expect(screen.getByRole('button', { name: 'Retry this Card archive' })).toBeEnabled();
});
it('aborts an in-flight archive when the route owner unmounts', () => {
  let signal!: AbortSignal; vi.stubGlobal('fetch', vi.fn((_path: string, init: RequestInit) => { signal = init.signal!; return new Promise(() => {}); }));
  const view = render(<CardArchiveControl {...props} />); review(); archive(); view.unmount(); expect(signal.aborted).toBe(true);
});
