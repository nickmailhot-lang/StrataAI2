import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { CardMoveControls } from './CardMoveControls';
import type { BoardSnapshot } from '../../api/workManagement';
const card = { id: 'card', title: 'Inspect roof', description: null, rank: '500000000000000000000000000000', version: 3 };
const snapshot: BoardSnapshot = { board: { id: 'board', organizationId: 'org', name: 'Repairs', description: null, lifecycleState: 'active' },
  access: { canView: true, canEdit: true, canMove: true, canAdminister: false }, lists: [
    { list: { id: 'source', name: 'Planning', rank: '1', lifecycleState: 'active' }, cards: [card] },
    { list: { id: 'dest', name: 'Complete', rank: '2', lifecycleState: 'active' }, cards: [] },
    { list: { id: 'archived', name: 'Archived list', rank: '3', lifecycleState: 'archived' }, cards: [] },
  ] };
const ack = { ...card, organizationId: 'org', boardId: 'board', listId: 'dest', version: 4 };
const reply = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
async function choose() {
  fireEvent.click(screen.getByRole('button', { name: 'Move card' }));
  fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Destination list' }));
  expect(screen.queryByRole('option', { name: 'Archived list' })).not.toBeInTheDocument();
  fireEvent.click(await screen.findByRole('option', { name: 'Complete' }));
}
afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals(); });
it('submits an admitted drop through the bound move and uncertain-response recovery path', async () => {
  const fetcher = vi.fn().mockRejectedValueOnce(new Error('Lost response')).mockResolvedValueOnce(reply(ack)); vi.stubGlobal('fetch', fetcher);
  const recovery = vi.fn(); const acknowledged = vi.fn();
  render(<CardMoveControls card={card} snapshot={snapshot} disabled={false} onAcknowledged={acknowledged} onRefresh={vi.fn()}
    onRecoveryChange={recovery} dropRequest={{ cardId: card.id, version: 3, destination: 'dest', before: '', nonce: 'drop-1' }} />);
  await screen.findByRole('button', { name: 'Retry this move' });
  expect(fetcher).toHaveBeenCalledTimes(1);
  const initial = fetcher.mock.calls[0][1];
  expect(JSON.parse(initial.body)).toEqual({ destinationListId: 'dest', expectedVersion: 3 });
  expect(recovery).toHaveBeenCalledWith(card.id, true);
  fireEvent.click(screen.getByRole('button', { name: 'Retry this move' }));
  await waitFor(() => expect(acknowledged).toHaveBeenCalledTimes(1));
  expect(fetcher.mock.calls[1][1].body).toBe(initial.body);
  expect(new Headers(fetcher.mock.calls[1][1].headers).get('Idempotency-Key')).toBe(new Headers(initial.headers).get('Idempotency-Key'));
  await waitFor(() => expect(recovery).toHaveBeenLastCalledWith(card.id, false));
});
it('rejects a drop captured before a newer card revision without writing', async () => {
  const fetcher = vi.fn(); vi.stubGlobal('fetch', fetcher); const refresh = vi.fn();
  render(<CardMoveControls card={{ ...card, version: 4 }} snapshot={snapshot} disabled={false} onAcknowledged={vi.fn()} onRefresh={refresh}
    dropRequest={{ cardId: card.id, version: 3, destination: 'dest', before: '', nonce: 'drop-2' }} />);
  expect(await screen.findByText(/The card changed during dragging/)).toBeVisible();
  expect(fetcher).not.toHaveBeenCalled(); expect(refresh).toHaveBeenCalledTimes(1);
});
it('publishes provisional placement only while saving, then clears it and reconciles an uncertain result', async () => {
  let reject: ((reason: Error) => void) | undefined;
  vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>((_, failed) => { reject = failed; })));
  const preview = vi.fn(); const refresh = vi.fn();
  render(<CardMoveControls card={card} snapshot={snapshot} disabled={false} onAcknowledged={vi.fn()} onRefresh={refresh} onPreview={preview} />);
  await choose(); fireEvent.click(screen.getByRole('button', { name: 'Confirm card move' }));
  expect(screen.getByRole('status')).toHaveTextContent('Placement is provisional');
  expect(preview).toHaveBeenLastCalledWith({ cardId: 'card', destination: 'dest', before: '' });
  await act(async () => { reject?.(new Error('Unknown result')); });
  await screen.findByRole('button', { name: 'Retry this move' });
  expect(preview).toHaveBeenLastCalledWith(); expect(refresh).toHaveBeenCalledOnce();
  expect(screen.queryByRole('status')).not.toBeInTheDocument();
});
it('requires a selected active destination and checks a bound move acknowledgment before refreshing', async () => {
  const fetcher = vi.fn().mockResolvedValue(reply(ack)); vi.stubGlobal('fetch', fetcher); const refresh = vi.fn();
  render(<CardMoveControls card={card} snapshot={snapshot} disabled={false} onAcknowledged={refresh} onRefresh={vi.fn()} />);
  fireEvent.click(screen.getByRole('button', { name: 'Move card' }));
  expect(screen.getByRole('button', { name: 'Confirm card move' })).toBeDisabled();
  fireEvent.click(screen.getByRole('button', { name: 'Cancel move' }));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Move card' })).toHaveFocus());
  await choose(); fireEvent.click(screen.getByRole('button', { name: 'Confirm card move' }));
  await screen.findByText('Move acknowledged. Current placement is being checked.');
  expect(refresh).toHaveBeenCalledOnce();
  expect(fetcher.mock.calls[0][0]).toBe('/cards/card/move');
  expect(JSON.parse(fetcher.mock.calls[0][1].body)).toEqual({ destinationListId: 'dest', expectedVersion: 3 });
  expect(fetcher.mock.calls[0][1].headers.get('X-StrataAI-Request')).toBe('1');
  expect(fetcher.mock.calls[0][1].headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
});
it('retains the exact intent and key after a lost response even when live state changes', async () => {
  const fetcher = vi.fn().mockRejectedValueOnce(new Error('Lost ack')).mockResolvedValueOnce(reply(ack)); vi.stubGlobal('fetch', fetcher);
  const refresh = vi.fn(); const preview = vi.fn();
  const props = { snapshot, disabled: false, onAcknowledged: refresh, onRefresh: vi.fn(), onPreview: preview };
  const view = render(<CardMoveControls {...props} card={card} />);
  await choose(); fireEvent.click(screen.getByRole('button', { name: 'Confirm card move' }));
  await screen.findByRole('button', { name: 'Retry this move' });
  expect(preview.mock.calls.filter(call => call[0] !== undefined)).toHaveLength(1);
  view.rerender(<CardMoveControls {...props} card={{ ...card, version: 8 }} />);
  expect(screen.getByRole('combobox', { name: 'Destination list' })).toHaveAttribute('aria-disabled', 'true');
  expect(screen.getByRole('combobox', { name: 'Card position' })).toHaveAttribute('aria-disabled', 'true');
  expect(screen.queryByRole('button', { name: 'Cancel move' })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Retry this move' }));
  await screen.findByText('Move acknowledged. Current placement is being checked.');
  expect(preview.mock.calls.filter(call => call[0] !== undefined)).toHaveLength(1);
  expect(fetcher.mock.calls[1][1].body).toBe(fetcher.mock.calls[0][1].body);
  expect(fetcher.mock.calls[1][1].headers.get('Idempotency-Key')).toBe(fetcher.mock.calls[0][1].headers.get('Idempotency-Key'));
  expect(refresh).toHaveBeenCalledOnce();
});
it('binds a relative position through an uncertain response even after its anchor disappears', async () => {
  const positioned: BoardSnapshot = { ...snapshot, lists: snapshot.lists.map(column => column.list.id === 'dest'
    ? { ...column, cards: [{ ...card, id: 'anchor', title: 'Check tiles' }] } : column) };
  const fetcher = vi.fn().mockRejectedValueOnce(new Error('Lost response')).mockResolvedValueOnce(reply(ack));
  vi.stubGlobal('fetch', fetcher);
  const props = { card, disabled: false, onAcknowledged: vi.fn(), onRefresh: vi.fn() };
  const view = render(<CardMoveControls {...props} snapshot={positioned} />);
  await choose();
  fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Card position' }));
  fireEvent.click(await screen.findByRole('option', { name: 'Before Check tiles' }));
  fireEvent.click(screen.getByRole('button', { name: 'Confirm card move' }));
  await screen.findByRole('button', { name: 'Retry this move' });
  expect(JSON.parse(fetcher.mock.calls[0][1].body)).toEqual({ destinationListId: 'dest', expectedVersion: 3, beforeCardId: 'anchor' });
  view.rerender(<CardMoveControls {...props} card={{ ...card, version: 8 }} snapshot={snapshot} />);
  fireEvent.click(screen.getByRole('button', { name: 'Retry this move' }));
  await screen.findByText('Move acknowledged. Current placement is being checked.');
  expect(fetcher.mock.calls[1][1].body).toBe(fetcher.mock.calls[0][1].body);
  expect(fetcher.mock.calls[1][1].headers.get('Idempotency-Key')).toBe(fetcher.mock.calls[0][1].headers.get('Idempotency-Key'));
});
it('blocks a vanished anchor before submission and allows choosing the end instead', async () => {
  const positioned: BoardSnapshot = { ...snapshot, lists: snapshot.lists.map(column => column.list.id === 'dest'
    ? { ...column, cards: [{ ...card, id: 'anchor', title: 'Check tiles' }] } : column) };
  const fetcher = vi.fn().mockResolvedValue(reply(ack)); vi.stubGlobal('fetch', fetcher);
  const props = { card, disabled: false, onAcknowledged: vi.fn(), onRefresh: vi.fn() };
  const view = render(<CardMoveControls {...props} snapshot={positioned} />); await choose();
  fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Card position' }));
  fireEvent.click(await screen.findByRole('option', { name: 'Before Check tiles' }));
  view.rerender(<CardMoveControls {...props} snapshot={snapshot} />);
  expect(screen.getByRole('button', { name: 'Confirm card move' })).toBeDisabled();
  expect(fetcher).not.toHaveBeenCalled();
  fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Card position' }));
  fireEvent.click(await screen.findByRole('option', { name: 'End of list' }));
  fireEvent.click(screen.getByRole('button', { name: 'Confirm card move' }));
  await screen.findByText('Move acknowledged. Current placement is being checked.');
  expect(JSON.parse(fetcher.mock.calls[0][1].body)).toEqual({ destinationListId: 'dest', expectedVersion: 3 });
});
it.each([{ ...ack, listId: 'foreign' }, { ...ack, boardId: 'other' }, { ...ack, organizationId: 'other' },
  { ...ack, version: 3 }, { ...ack, rank: 'private content' }])('does not acknowledge an invalid or cross-scope move result: %j', async result => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(reply(result))); const refresh = vi.fn();
  render(<CardMoveControls card={card} snapshot={snapshot} disabled={false} onAcknowledged={refresh} onRefresh={vi.fn()} />);
  await choose(); fireEvent.click(screen.getByRole('button', { name: 'Confirm card move' }));
  await screen.findByRole('button', { name: 'Retry this move' }); expect(refresh).not.toHaveBeenCalled();
});
it('blocks a stale review without a request and requires current Board recovery on a rejected move', async () => {
  const fetcher = vi.fn().mockResolvedValue(reply({}, 409)); vi.stubGlobal('fetch', fetcher); const refresh = vi.fn();
  const props = { snapshot, disabled: false, onAcknowledged: vi.fn(), onRefresh: refresh };
  const view = render(<CardMoveControls {...props} card={card} />); await choose();
  view.rerender(<CardMoveControls {...props} card={{ ...card, version: 4 }} />);
  expect(screen.getByRole('button', { name: 'Confirm card move' })).toBeDisabled(); expect(fetcher).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole('button', { name: 'Check current Board' }));
  await choose(); fireEvent.click(screen.getByRole('button', { name: 'Confirm card move' }));
  await screen.findByText('The card changed. Check the current Board before reviewing another move.');
  expect(fetcher).toHaveBeenCalledOnce(); expect(refresh).toHaveBeenCalledTimes(2);
  expect(screen.getByRole('button', { name: 'Confirm card move' })).toBeDisabled();
});
it('bounds a stalled request and fences a late acknowledgment after unmount', async () => {
  let finish: ((value: Response) => void) | undefined;
  vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(resolve => { finish = resolve; }))); const refresh = vi.fn();
  const view = render(<CardMoveControls card={card} snapshot={snapshot} disabled={false} onAcknowledged={refresh} onRefresh={vi.fn()} />);
  await choose(); vi.useFakeTimers(); fireEvent.click(screen.getByRole('button', { name: 'Confirm card move' }));
  await act(() => vi.advanceTimersByTimeAsync(15_000));
  expect(screen.getByRole('button', { name: 'Retry this move' })).toBeEnabled(); view.unmount();
  await act(async () => { finish!(reply(ack)); }); expect(refresh).not.toHaveBeenCalled();
});
