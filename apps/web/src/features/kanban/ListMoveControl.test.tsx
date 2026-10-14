import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { ListMoveControl } from './ListMoveControl';
import type { BoardSnapshot } from '../../api/workManagement';

const board = '11111111-1111-1111-1111-111111111111', destination = '22222222-2222-2222-2222-222222222222';
const id = '33333333-3333-3333-3333-333333333333', org = '44444444-4444-4444-4444-444444444444';
const user = '55555555-5555-5555-5555-555555555555', rank = '500000000000000000000000000000';
const profile = { id: user, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'America/Vancouver' };
const list = { id, name: 'Planning', rank, version: 2, lifecycleState: 'active' };
const snapshot: BoardSnapshot = { board: { id: board, organizationId: org, name: 'Source', description: null, lifecycleState: 'active' },
  access: { canView: true, canEdit: true, canMove: true, canAdminister: false }, lists: [{ list, cards: [] }] };
const target = { ...snapshot, board: { ...snapshot.board, id: destination, name: 'Delivery' }, lists: [] };
const directory = [{ id: board, name: 'Source', version: 1 }, { id: destination, name: 'Delivery', version: 1 }];
const ack = { id, organizationId: org, boardId: destination, name: 'Planning', rank, lifecycleState: 'active', version: 3 };
const props = { snapshot, disabled: false, unavailableListIds: new Set<string>(), onBusyChange: vi.fn(), onRecoveryChange: vi.fn(),
  onRefresh: vi.fn(), onReturnFocus: vi.fn() };
const reply = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
function fixture(responses: (Response | Error | (() => Promise<Response>))[], account = () => profile) {
  const requests = vi.fn(() => {
    const next = responses.shift();
    if (next instanceof Error) return Promise.reject(next);
    if (typeof next === 'function') return next();
    return Promise.resolve(next);
  });
  const fetch = vi.fn((path: string) => path === '/me' ? Promise.resolve(reply(account())) : requests());
  vi.stubGlobal('fetch', fetch); return fetch;
}
function select(label: string, option: string) {
  fireEvent.mouseDown(screen.getByRole('combobox', { name: label })); fireEvent.click(screen.getByRole('option', { name: option }));
}
async function review() {
  fireEvent.click(screen.getByRole('button', { name: 'Move list' }));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Reload move destinations' })).toBeEnabled());
  select('List to move', 'Planning'); select('Destination Board', 'Delivery');
  fireEvent.click(screen.getByRole('button', { name: 'Review List move' }));
  await screen.findByText('Move Planning to the end of Delivery?');
}
function confirm() { fireEvent.click(screen.getByRole('button', { name: 'Confirm List move' })); }
afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); vi.clearAllMocks(); });

it('checks account and destination move authority, submits the original source/revision/key, and returns focus after acknowledgment', async () => {
  const fetch = fixture([reply(directory), reply(target), reply(ack)]);
  render(<ListMoveControl {...props} />); await review(); confirm();
  await screen.findByText('List move acknowledged. Check the destination Board for its current contents.');
  const write = fetch.mock.calls.find(call => call[0] === `/lists/${id}/move`)!;
  const init = (write as unknown as [string, RequestInit])[1];
  expect(JSON.parse(init.body as string)).toEqual({ sourceBoardId: board, destinationBoardId: destination, expectedVersion: 2 });
  expect(new Headers(init.headers).get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
  expect(props.onRefresh).toHaveBeenCalledOnce();
  await waitFor(() => expect(props.onReturnFocus).toHaveBeenCalledOnce());
});
it('keeps the same move request after reply loss and canonical source removal, excluding replacement actions', async () => {
  const fetch = fixture([reply(directory), reply(target), new Error('Lost response'), reply(ack)]);
  const view = render(<ListMoveControl {...props} />); await review(); confirm();
  await screen.findByRole('button', { name: 'Retry this List move' });
  view.rerender(<ListMoveControl {...props} snapshot={{ ...snapshot, lists: [] }} />);
  expect(screen.queryByRole('button', { name: 'Cancel move' })).not.toBeInTheDocument();
  expect(screen.queryByRole('combobox', { name: 'List to move' })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Retry this List move' }));
  await screen.findByText('List move acknowledged. Check the destination Board for its current contents.');
  const writes = fetch.mock.calls.filter(call => call[0] === `/lists/${id}/move`) as unknown as [string, RequestInit][];
  expect(writes).toHaveLength(2); expect(writes[1][1].body).toBe(writes[0][1].body);
  expect(new Headers(writes[1][1].headers).get('Idempotency-Key')).toBe(new Headers(writes[0][1].headers).get('Idempotency-Key'));
});
it.each([['id', board], ['organizationId', destination], ['boardId', board], ['name', 'Other'], ['version', 2],
  ['lifecycleState', 'archived'], ['rank', 'wrong']])('retains recovery for an acknowledgment with wrong %s', async (field, value) => {
  fixture([reply(directory), reply(target), reply({ ...ack, [field]: value })]);
  render(<ListMoveControl {...props} />); await review(); confirm();
  await screen.findByRole('button', { name: 'Retry this List move' });
  expect(screen.queryByText(/List move acknowledged/)).not.toBeInTheDocument();
});
it('preserves the destination draft but requires a renewed review after source changes or pending Board reads', async () => {
  fixture([reply(directory), reply(target)]); const view = render(<ListMoveControl {...props} />); await review();
  view.rerender(<ListMoveControl {...props} refreshing />);
  expect(screen.getByRole('button', { name: 'Confirm List move' })).toBeDisabled();
  view.rerender(<ListMoveControl {...props} snapshot={{ ...snapshot, lists: [{ list: { ...list, version: 4 }, cards: [] }] }} />);
  expect(screen.getByRole('button', { name: 'Confirm List move' })).toBeDisabled();
  expect(screen.getByRole('combobox', { name: 'Destination Board' })).toHaveTextContent('Delivery');
  fireEvent.click(screen.getByRole('button', { name: 'Use current List for move review' }));
  expect(screen.getByRole('button', { name: 'Confirm List move' })).toBeDisabled();
  expect(screen.getByRole('button', { name: 'Review List move' })).toBeEnabled();
});
it('refuses a target lacking move rights without rendering its protected name or submitting', async () => {
  const fetch = fixture([reply(directory), reply({ ...target, board: { ...target.board, name: 'Protected title' },
    access: { ...target.access, canMove: false } })]);
  render(<ListMoveControl {...props} />); fireEvent.click(screen.getByRole('button', { name: 'Move list' }));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Reload move destinations' })).toBeEnabled());
  select('List to move', 'Planning'); select('Destination Board', 'Delivery'); fireEvent.click(screen.getByRole('button', { name: 'Review List move' }));
  await screen.findByText('The destination could not be checked for moving. Review it again.');
  expect(screen.queryByText('Protected title')).not.toBeInTheDocument();
  expect(fetch.mock.calls.some(call => call[0] === `/lists/${id}/move`)).toBe(false);
});
it('withdraws a reviewed request on account change before writing', async () => {
  let current = profile; const fetch = fixture([reply(directory), reply(target)], () => current);
  render(<ListMoveControl {...props} />); await review(); current = { ...profile, id: destination }; confirm();
  await screen.findByText('Your account changed. Reopen List movement.');
  expect(fetch.mock.calls.some(call => call[0] === `/lists/${id}/move`)).toBe(false);
  expect(screen.queryByText('Move Planning to the end of Delivery?')).not.toBeInTheDocument();
});
it('does not submit after a late account check resolves following permission withdrawal', async () => {
  let resolve!: (value: Response) => void; let stall = false;
  const fetch = fixture([reply(directory), reply(target)]);
  const normal = fetch.getMockImplementation()!;
  fetch.mockImplementation((path: string) => path === '/me' && stall
    ? new Promise<Response>(done => { resolve = done; }) : normal(path));
  const view = render(<ListMoveControl {...props} />); await review(); stall = true; confirm();
  await waitFor(() => expect(resolve).toBeTypeOf('function'));
  view.rerender(<ListMoveControl {...props} snapshot={{ ...snapshot, access: { ...snapshot.access, canMove: false } }} />);
  await act(async () => { resolve(reply(profile)); });
  expect(fetch.mock.calls.some(call => call[0] === `/lists/${id}/move`)).toBe(false);
  expect(screen.queryByRole('button', { name: 'Retry this List move' })).not.toBeInTheDocument();
});
it('aborts a pending move after permission loss and ignores a late acknowledgment', async () => {
  let resolve!: (value: Response) => void;
  const fetch = fixture([reply(directory), reply(target), () => new Promise<Response>(done => { resolve = done; })]);
  const view = render(<ListMoveControl {...props} />); await review(); confirm();
  await waitFor(() => expect(resolve).toBeTypeOf('function'));
  const write = fetch.mock.calls.find(call => call[0] === `/lists/${id}/move`) as unknown as [string, RequestInit];
  view.rerender(<ListMoveControl {...props} snapshot={{ ...snapshot, access: { ...snapshot.access, canMove: false } }} />);
  expect(write[1].signal?.aborted).toBe(true); await act(async () => { resolve(reply(ack)); });
  expect(screen.queryByRole('button', { name: 'Retry this List move' })).not.toBeInTheDocument();
  expect(screen.queryByText(/List move acknowledged/)).not.toBeInTheDocument();
});
it('refuses conflict details and requires another review', async () => {
  fixture([reply(directory), reply(target), reply({ title: 'Private SQL details' }, 409)]);
  render(<ListMoveControl {...props} />); await review(); confirm();
  await screen.findByText('This move could not be applied. Check the Board and review a current List.');
  expect(screen.queryByText('Private SQL details')).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Retry this List move' })).not.toBeInTheDocument();
});
it('bounds a stalled write and retains recovery', async () => {
  fixture([reply(directory), reply(target), () => new Promise<Response>(() => undefined)]);
  render(<ListMoveControl {...props} />); await review(); vi.useFakeTimers(); confirm();
  await act(async () => { await vi.advanceTimersByTimeAsync(15_001); }); vi.useRealTimers();
  await screen.findByRole('button', { name: 'Retry this List move' });
});
it('isolates recovery by Board scope and ignores late discovery after navigation', async () => {
  let resolve!: (value: Response) => void;
  fixture([() => new Promise<Response>(done => { resolve = done; })]);
  const view = render(<ListMoveControl {...props} />); fireEvent.click(screen.getByRole('button', { name: 'Move list' }));
  await waitFor(() => expect(resolve).toBeTypeOf('function'));
  view.rerender(<ListMoveControl {...props} snapshot={target} />); await act(async () => { resolve(reply(directory)); });
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  expect(screen.queryByText('Delivery', { selector: 'li' })).not.toBeInTheDocument();
});
