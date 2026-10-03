import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { CardLabelPicker } from './CardLabelPicker';
import type { BoardSnapshot, WorkCard } from '../../api/workManagement';
const org = '11111111-1111-1111-1111-111111111111', board = '22222222-2222-2222-2222-222222222222', id = '33333333-3333-3333-3333-333333333333', labelId = '44444444-4444-4444-4444-444444444444';
const card: WorkCard = { id, title: 'Card', description: null, rank: '500000000000000000000000000000', version: 1 };
const snapshot: BoardSnapshot = { board: { id: board, organizationId: org, name: 'Board', description: null, lifecycleState: 'active' }, access: { canView: true, canEdit: true, canAdminister: true, canMove: true }, lists: [{ list: { id: 'list', name: 'List', rank: card.rank, lifecycleState: 'active' }, cards: [card] }] };
const props = () => ({ cardId: id, card, snapshot, disabled: false, onBusyChange: vi.fn(), onRecoveryChange: vi.fn(), onRefresh: vi.fn() });
const options = { cardId: id, organizationId: org, boardId: board, cardVersion: 1, nextCursor: null, items: [{ label: { id: labelId, organizationId: org, boardId: board, name: 'Priority', color: 'red', deleted: false }, assigned: false }] };
const ack = { card: { ...card, organizationId: org, boardId: board, version: 2 }, labelId, assigned: true, changed: true };
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
async function open() { fireEvent.click(screen.getByRole('button', { name: 'Edit Card labels' })); return screen.findByRole('button', { name: 'Add label Priority' }); }
afterEach(() => vi.unstubAllGlobals());
it('loads current choices and sends a versioned assignment, then restores focus', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(response(options)).mockResolvedValueOnce(response(ack)); vi.stubGlobal('fetch', fetch); const p = props(); render(<CardLabelPicker {...p} />);
  fireEvent.click(await open()); await waitFor(() => expect(p.onRefresh).toHaveBeenCalled());
  expect(fetch.mock.calls[1][0]).toBe(`/cards/${id}/labels/${labelId}?version=1`); expect(fetch.mock.calls[1][1].method).toBe('PUT');
  await waitFor(() => expect(screen.getByRole('button', { name: 'Edit Card labels' })).toHaveFocus());
});
it('removes an assigned option with DELETE', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(response({ ...options, items: [{ ...options.items[0], assigned: true }] })).mockResolvedValueOnce(response({ ...ack, assigned: false })); vi.stubGlobal('fetch', fetch);
  const p = props(); render(<CardLabelPicker {...p} />); fireEvent.click(screen.getByRole('button', { name: 'Edit Card labels' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Remove label Priority' })); await waitFor(() => expect(p.onRefresh).toHaveBeenCalled()); expect(fetch.mock.calls[1][1].method).toBe('DELETE');
});
it('preserves the original assignment key and revision after loss and Card refresh', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(response(options)).mockRejectedValueOnce(new Error('lost')).mockResolvedValueOnce(response(ack)); vi.stubGlobal('fetch', fetch);
  const p = props(); const view = render(<CardLabelPicker {...p} />); fireEvent.click(await open()); await screen.findByText('Retry label change');
  view.rerender(<CardLabelPicker {...p} card={{ ...card, version: 3 }} />);
  expect(p.onRecoveryChange).toHaveBeenCalledWith(true); expect(screen.queryByText('Done editing labels')).not.toBeInTheDocument();
  fireEvent.click(screen.getByText('Retry label change')); await waitFor(() => expect(p.onRefresh).toHaveBeenCalled());
  expect(fetch.mock.calls[2][0]).toBe(fetch.mock.calls[1][0]);
  expect(new Headers(fetch.mock.calls[2][1].headers).get('Idempotency-Key')).toBe(new Headers(fetch.mock.calls[1][1].headers).get('Idempotency-Key'));
});
it('blocks stale loaded options when a newer Card arrives', async () => {
  const fetch = vi.fn().mockResolvedValue(response(options)); vi.stubGlobal('fetch', fetch); const p = props(); const view = render(<CardLabelPicker {...p} />); await open();
  view.rerender(<CardLabelPicker {...p} card={{ ...card, version: 2 }} />); expect(screen.queryByRole('button', { name: 'Add label Priority' })).not.toBeInTheDocument(); expect(fetch).toHaveBeenCalledTimes(1);
});
it('clears denied options without displaying server content', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response({ detail: 'Private server detail' }, 403))); const p = props(); render(<CardLabelPicker {...p} />);
  fireEvent.click(screen.getByRole('button', { name: 'Edit Card labels' })); await screen.findByRole('alert'); expect(p.onRefresh).toHaveBeenCalled(); expect(screen.queryByText('Private server detail')).not.toBeInTheDocument();
});
it('rejects mismatched scope before showing choices', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response({ ...options, boardId: org }))); render(<CardLabelPicker {...props()} />);
  fireEvent.click(screen.getByRole('button', { name: 'Edit Card labels' })); await screen.findByRole('alert'); expect(screen.queryByText('Priority (red)')).not.toBeInTheDocument();
});
it('does not resurrect a pending assignment after permission loss', async () => {
  let fail!: (error: Error) => void;
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(response(options)).mockImplementationOnce(() => new Promise((_resolve, reject) => { fail = reject; })));
  const p = props(); const view = render(<CardLabelPicker {...p} />); fireEvent.click(await open()); await waitFor(() => expect(fail).toBeDefined());
  view.rerender(<CardLabelPicker {...p} snapshot={{ ...snapshot, access: { ...snapshot.access, canEdit: false } }} />);
  await act(async () => fail(new Error('late'))); expect(screen.queryByText('Retry label change')).not.toBeInTheDocument(); expect(p.onRefresh).not.toHaveBeenCalled();
});

it('restores retry focus after temporary Board admission checks without stealing an intentional focus change', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(response(options)).mockRejectedValueOnce(new Error('lost'));
  vi.stubGlobal('fetch', fetch); const p = props();
  const view = render(<><button>Other control</button><CardLabelPicker {...p} /></>);
  fireEvent.click(await open());
  const retry = await screen.findByRole('button', { name: 'Retry label change' });
  await waitFor(() => expect(retry).toHaveFocus());
  view.rerender(<><button>Other control</button><CardLabelPicker {...p} disabled /></>);
  expect(retry).toBeDisabled(); retry.blur();
  view.rerender(<><button>Other control</button><CardLabelPicker {...p} /></>);
  await waitFor(() => expect(retry).toHaveFocus());
  screen.getByRole('button', { name: 'Other control' }).focus();
  view.rerender(<><button>Other control</button><CardLabelPicker {...p} disabled /></>);
  view.rerender(<><button>Other control</button><CardLabelPicker {...p} /></>);
  expect(screen.getByRole('button', { name: 'Other control' })).toHaveFocus();
  expect(fetch).toHaveBeenCalledTimes(2); expect(p.onRecoveryChange).toHaveBeenCalledWith(true);
});
