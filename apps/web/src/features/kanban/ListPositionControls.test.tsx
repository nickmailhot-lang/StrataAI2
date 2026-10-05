import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { BoardSnapshot } from '../../api/workManagement';
import { ListPositionControls } from './ListPositionControls';

const list = { id: 'moving', name: 'Planning', rank: '500000000000000000000000000000', version: 1, lifecycleState: 'active' };
const snapshot: BoardSnapshot = { board: { id: 'board', organizationId: 'org', name: 'Board', description: null, lifecycleState: 'active' },
  access: { canView: true, canEdit: true, canMove: true, canAdminister: false },
  lists: [{ list, cards: [] }, { list: { ...list, id: 'anchor', name: 'Complete' }, cards: [] }] };
const ack = { ...list, organizationId: 'org', boardId: 'board', rank: '400000000000000000000000000000', version: 2 };
const reply = (value: unknown) => new Response(JSON.stringify(value));
const props = { list, snapshot, disabled: false, onRefresh: vi.fn(), onBusyChange: vi.fn() };
async function choose() {
  fireEvent.click(screen.getByRole('button', { name: 'Move Planning list' }));
  fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Position for Planning' }));
  fireEvent.click(await screen.findByRole('option', { name: 'Before Complete' }));
}
afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals(); vi.clearAllMocks(); });
it('persists an admitted drop directly through the same scoped move protocol', async () => {
  const fetcher = vi.fn().mockResolvedValue(reply(ack)); vi.stubGlobal('fetch', fetcher);
  render(<ListPositionControls {...props} dropRequest={{ listId: list.id, name: list.name, version: 1, before: 'anchor', nonce: 'drop-1' }} />);
  await screen.findByText('List move acknowledged. Current ordering is being checked.');
  expect(JSON.parse(fetcher.mock.calls[0][1].body)).toEqual({ name: 'Planning', version: 1, beforeListId: 'anchor' });
  expect(fetcher).toHaveBeenCalledOnce();
});
it('does not persist a drop captured before a newer canonical revision', async () => {
  const fetcher = vi.fn(); vi.stubGlobal('fetch', fetcher);
  render(<ListPositionControls {...props} list={{ ...list, version: 2 }} dropRequest={{ listId: list.id, name: list.name, version: 1, before: 'anchor', nonce: 'drop-1' }} />);
  await screen.findByText('This list changed while dragging. Check current ordering before moving it.');
  expect(fetcher).not.toHaveBeenCalled(); expect(props.onRefresh).toHaveBeenCalledOnce();
});
it('submits a reviewed position with the current name/version and a bound key, then reads current order', async () => {
  const fetcher = vi.fn().mockResolvedValue(reply(ack)); vi.stubGlobal('fetch', fetcher);
  const view = render(<ListPositionControls {...props} />); await choose();
  fireEvent.click(screen.getByRole('button', { name: 'Confirm list move' }));
  await screen.findByText('List move acknowledged. Current ordering is being checked.');
  view.rerender(<ListPositionControls {...props} list={ack} />);
  await waitFor(() => expect(screen.getByRole('button', { name: 'Move Planning list' })).toHaveFocus());
  expect(fetcher.mock.calls[0][0]).toBe('/lists/moving');
  expect(JSON.parse(fetcher.mock.calls[0][1].body)).toEqual({ name: 'Planning', version: 1, beforeListId: 'anchor' });
  expect(fetcher.mock.calls[0][1].headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
  expect(props.onRefresh).toHaveBeenCalledOnce();
});
it('retains drop focus recovery until the acknowledged canonical revision is admitted', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(reply(ack)));
  const dropRequest = { listId: list.id, name: list.name, version: 1, before: 'anchor', nonce: 'focus-drop' };
  const view = render(<ListPositionControls {...props} dropRequest={dropRequest} />);
  await screen.findByText('List move acknowledged. Current ordering is being checked.');
  const action = screen.getByRole('button', { name: 'Move Planning list' });
  expect(action).not.toHaveFocus();
  view.rerender(<ListPositionControls {...props} dropRequest={dropRequest} list={ack} disabled />);
  expect(action).not.toHaveFocus();
  view.rerender(<ListPositionControls {...props} dropRequest={dropRequest} list={ack} />);
  await waitFor(() => expect(action).toHaveFocus());
});
it('preserves original name/position/version/key after uncertainty and a newer canonical rename', async () => {
  const fetcher = vi.fn().mockRejectedValueOnce(new Error('Lost')).mockResolvedValueOnce(reply(ack)); vi.stubGlobal('fetch', fetcher);
  const preview = vi.fn();
  const view = render(<ListPositionControls {...props} onPreview={preview} />); await choose();
  fireEvent.click(screen.getByRole('button', { name: 'Confirm list move' }));
  await screen.findByRole('button', { name: 'Retry this list move' });
  expect(preview.mock.calls.filter(call => call[0] !== undefined)).toHaveLength(1);
  expect(preview).toHaveBeenLastCalledWith();
  view.rerender(<ListPositionControls {...props} onPreview={preview} list={{ ...list, name: 'Renamed', version: 8 }} />);
  expect(screen.getByRole('combobox')).toHaveAttribute('aria-disabled', 'true');
  expect(screen.queryByRole('button', { name: 'Cancel list move' })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Retry this list move' }));
  await screen.findByText('List move acknowledged. Current ordering is being checked.');
  expect(preview.mock.calls.filter(call => call[0] !== undefined)).toHaveLength(1);
  expect(fetcher.mock.calls[1][1].body).toBe(fetcher.mock.calls[0][1].body);
  expect(fetcher.mock.calls[1][1].headers.get('Idempotency-Key')).toBe(fetcher.mock.calls[0][1].headers.get('Idempotency-Key'));
});
it('blocks a vanished unsubmitted position and restores focus after cancellation', async () => {
  const fetcher = vi.fn(); vi.stubGlobal('fetch', fetcher); const view = render(<ListPositionControls {...props} />); await choose();
  view.rerender(<ListPositionControls {...props} snapshot={{ ...snapshot, lists: [snapshot.lists[0]] }} />);
  expect(screen.getByRole('button', { name: 'Confirm list move' })).toBeDisabled(); expect(fetcher).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole('button', { name: 'Cancel list move' }));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Move Planning list' })).toHaveFocus());
});
it('treats a cross-Board acknowledgment as uncertain rather than accepting it', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(reply({ ...ack, boardId: 'other' })));
  render(<ListPositionControls {...props} />); await choose(); fireEvent.click(screen.getByRole('button', { name: 'Confirm list move' }));
  await screen.findByRole('button', { name: 'Retry this list move' });
  expect(screen.queryByText('List move acknowledged. Current ordering is being checked.')).not.toBeInTheDocument();
});
it('bounds stalled saving and allows explicit bound recovery', async () => {
  vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(() => {})));
  const view = render(<ListPositionControls {...props} />); await choose(); vi.useFakeTimers();
  fireEvent.click(screen.getByRole('button', { name: 'Confirm list move' }));
  await act(async () => { await vi.advanceTimersByTimeAsync(15_000); });
  expect(screen.getByRole('button', { name: 'Retry this list move' })).toBeEnabled();
  view.unmount();
});
