import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { CrossBoardCardMoveControl } from './CrossBoardCardMoveControl';
import type { BoardSnapshot } from '../../api/workManagement';

const id = (n: number) => `00000000-0000-4000-8000-${n.toString().padStart(12, '0')}`;
const card = { id: id(1), title: '<img src=x> Card', description: null, rank: '500000000000000000000000000000', version: 4 };
const snapshot: BoardSnapshot = { board: { id: id(2), organizationId: id(3), name: 'Source', description: null, lifecycleState: 'active' },
  access: { canView: true, canEdit: true, canMove: true, canAdminister: true }, lists: [{ list: { id: id(4), name: 'Source List', rank: '1', lifecycleState: 'active' }, cards: [card] }] };
const target: BoardSnapshot = { ...snapshot, board: { ...snapshot.board, id: id(5), name: '<script> Destination' }, lists: [
  { list: { id: id(6), name: 'Destination', rank: '1', lifecycleState: 'active' }, cards: [] },
  { list: { id: id(7), name: 'Archived', rank: '2', lifecycleState: 'archived' }, cards: [] }] };
const ack = { ...card, organizationId: id(3), boardId: id(5), listId: id(6), version: 5 };
const reply = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
const props = { card, selectedCardId: card.id, snapshot, disabled: false, unavailable: false,
  onBusyChange: vi.fn(), onRecoveryChange: vi.fn(), onRefresh: vi.fn() };
afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals(); vi.clearAllMocks(); });
function mock(move: (options: RequestInit) => Promise<Response> = () => Promise.resolve(reply(ack))) {
  const mutations: RequestInit[] = [];
  const fetcher = vi.fn((path: string, options: RequestInit) => {
    if (path === '/me') return Promise.resolve(reply({ id: id(8) }));
    if (path === `/organizations/${id(3)}/boards`) return Promise.resolve(reply([{ id: id(2), name: 'Source', version: 1 }, { id: id(5), name: '<script> Destination', version: 1 }]));
    if (path === `/boards/${id(5)}`) return Promise.resolve(reply(target));
    if (path === `/cards/${card.id}/move`) { mutations.push(options); return move(options); }
    throw new Error('Unexpected request');
  });
  vi.stubGlobal('fetch', fetcher); return { fetcher, mutations };
}
async function choose() {
  fireEvent.click(screen.getByRole('button', { name: 'Move to another Board' }));
  await waitFor(() => expect(screen.getByRole('combobox', { name: 'Destination Board' })).not.toHaveAttribute('aria-disabled', 'true'));
  fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Destination Board' }));
  expect(screen.queryByRole('option', { name: 'Source' })).not.toBeInTheDocument();
  fireEvent.click(await screen.findByRole('option', { name: '<script> Destination' }));
  await waitFor(() => expect(screen.getByRole('combobox', { name: 'Destination List on another Board' })).not.toHaveAttribute('aria-disabled', 'true'));
  fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Destination List on another Board' }));
  expect(screen.queryByRole('option', { name: 'Archived' })).not.toBeInTheDocument();
  fireEvent.click(await screen.findByRole('option', { name: 'Destination' }));
}
it('requires an admitted destination and binds original source and version to the command', async () => {
  const { mutations } = mock(); const view = render(<CrossBoardCardMoveControl {...props} />);
  await choose(); expect(view.container.querySelector('script')).toBeNull(); expect(view.container.querySelector('img')).toBeNull();
  fireEvent.click(screen.getByRole('button', { name: 'Confirm move to another Board' }));
  await screen.findByText('Move acknowledged. Check the destination Board for current placement.');
  expect(mutations).toHaveLength(1); expect(JSON.parse(mutations[0].body as string)).toEqual({ sourceBoardId: id(2), destinationListId: id(6), expectedVersion: 4 });
  expect(new Headers(mutations[0].headers).get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
  expect(props.onRefresh).toHaveBeenCalledOnce();
});
it('recovers the original intent after canonical source removal and later revision changes', async () => {
  let attempts = 0; const { mutations } = mock(() => ++attempts === 1 ? Promise.reject(new Error('Lost response')) : Promise.resolve(reply(ack)));
  const view = render(<CrossBoardCardMoveControl {...props} />); await choose();
  fireEvent.click(screen.getByRole('button', { name: 'Confirm move to another Board' }));
  await screen.findByRole('button', { name: 'Retry this cross-Board move' });
  view.rerender(<CrossBoardCardMoveControl {...props} card={undefined} snapshot={{ ...snapshot, lists: [] }} />);
  expect(screen.queryByRole('button', { name: 'Cancel cross-Board move' })).not.toBeInTheDocument();
  expect(screen.getByRole('combobox', { name: 'Destination Board' })).toHaveAttribute('aria-disabled', 'true');
  fireEvent.click(screen.getByRole('button', { name: 'Retry this cross-Board move' }));
  await screen.findByText('Move acknowledged. Check the destination Board for current placement.');
  expect(mutations).toHaveLength(2); expect(mutations[1].body).toBe(mutations[0].body);
  expect(new Headers(mutations[1].headers).get('Idempotency-Key')).toBe(new Headers(mutations[0].headers).get('Idempotency-Key'));
});
it('prevents a stale review from submitting after current Card version changes', async () => {
  const { mutations } = mock(); const view = render(<CrossBoardCardMoveControl {...props} />); await choose();
  view.rerender(<CrossBoardCardMoveControl {...props} card={{ ...card, version: 5 }} />);
  expect(screen.getByRole('button', { name: 'Confirm move to another Board' })).toBeDisabled(); expect(mutations).toHaveLength(0);
});
it.each([401, 403, 404])('clears destination details and unresolved intent after terminal refusal %i', async status => {
  mock(() => Promise.resolve(reply({}, status))); render(<CrossBoardCardMoveControl {...props} />); await choose();
  fireEvent.click(screen.getByRole('button', { name: 'Confirm move to another Board' }));
  await screen.findByText('This move is unavailable.'); expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
  expect(screen.queryByText(/Move <img/)).not.toBeInTheDocument(); expect(screen.queryByRole('button', { name: 'Retry this cross-Board move' })).not.toBeInTheDocument();
});
it('clears a pending original intent before transmitting under a different signed-in account', async () => {
  const { fetcher, mutations } = mock(() => Promise.reject(new Error('Lost response')));
  render(<CrossBoardCardMoveControl {...props} />); await choose(); fireEvent.click(screen.getByRole('button', { name: 'Confirm move to another Board' }));
  await screen.findByRole('button', { name: 'Retry this cross-Board move' });
  fetcher.mockImplementation((path: string) => { if (path === '/me') return Promise.resolve(reply({ id: id(9) })); throw new Error('Must not transmit'); });
  fireEvent.click(screen.getByRole('button', { name: 'Retry this cross-Board move' }));
  await screen.findByText('This move is unavailable.'); expect(mutations).toHaveLength(1);
});
it('fences late destination results after unmount', async () => {
  const { fetcher } = mock(); let finish: ((value: Response) => void) | undefined;
  fetcher.mockImplementation((path: string) => path === '/me' ? Promise.resolve(reply({ id: id(8) })) : new Promise(resolve => { finish = resolve; }));
  const view = render(<CrossBoardCardMoveControl {...props} />); fireEvent.click(screen.getByRole('button', { name: 'Move to another Board' }));
  await waitFor(() => expect(finish).toBeDefined()); view.unmount();
  await act(async () => finish!(reply([{ id: id(5), name: 'Late', version: 1 }])));
  expect(props.onBusyChange).toHaveBeenLastCalledWith(false); expect(props.onRecoveryChange).toHaveBeenLastCalledWith(false);
});
