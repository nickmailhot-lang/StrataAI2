import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { ListCopyControl } from './ListCopyControl';
import type { BoardSnapshot } from '../../api/workManagement';

const board = '11111111-1111-1111-1111-111111111111'; const destination = '22222222-2222-2222-2222-222222222222';
const sourceId = '33333333-3333-3333-3333-333333333333'; const copyId = '44444444-4444-4444-4444-444444444444';
const org = '55555555-5555-5555-5555-555555555555'; const rank = '500000000000000000000000000000';
const list = { id: sourceId, name: 'Planning', rank, version: 2, lifecycleState: 'active' };
const snapshot: BoardSnapshot = { board: { id: board, organizationId: org, name: 'Source Board', description: null, lifecycleState: 'active' },
  access: { canView: true, canEdit: true, canMove: true, canAdminister: false }, lists: [{ list, cards: [] }] };
const target = { ...snapshot, board: { ...snapshot.board, id: destination, name: 'Delivery' }, lists: [] };
const directory = [{ id: board, name: 'Source Board', version: 1 }, { id: destination, name: 'Delivery', version: 1 }];
const ack = { id: copyId, organizationId: org, boardId: destination, name: 'Reviewed copy', rank, lifecycleState: 'active', version: 1 };
const props = { snapshot, disabled: false, unavailableListIds: new Set<string>(), onBusyChange: vi.fn(),
  onRecoveryChange: vi.fn(), onRefresh: vi.fn(), onReturnFocus: vi.fn() };
const reply = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
function select(label: string, option: string) {
  fireEvent.mouseDown(screen.getByRole('combobox', { name: label }));
  fireEvent.click(screen.getByRole('option', { name: option }));
}
async function review() {
  fireEvent.click(screen.getByRole('button', { name: 'Copy list' }));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Reload copy destinations' })).toBeEnabled());
  select('List to copy', 'Planning');
  fireEvent.change(screen.getByRole('textbox', { name: 'Copy name' }), { target: { value: '  Reviewed copy  ' } });
  select('Destination Board', 'Delivery');
  fireEvent.click(screen.getByRole('button', { name: 'Review List copy' }));
  await screen.findByText('Copy Planning as Reviewed copy to Delivery?');
}
function confirm() { fireEvent.click(screen.getByRole('button', { name: 'Confirm List copy' })); }
afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); vi.clearAllMocks(); });

it('opens discovery during a live Board read but waits for its current source revision before confirming', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(reply(directory)).mockResolvedValueOnce(reply(target));
  vi.stubGlobal('fetch', fetch);
  const view = render(<ListCopyControl {...props} refreshing />);
  fireEvent.click(screen.getByRole('button', { name: 'Copy list' }));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Reload copy destinations' })).toBeEnabled());
  select('List to copy', 'Planning'); select('Destination Board', 'Delivery');
  expect(screen.getByRole('button', { name: 'Review List copy' })).toBeDisabled();
  expect(screen.getByRole('button', { name: 'Confirm List copy' })).toBeDisabled();
  expect(fetch).toHaveBeenCalledTimes(1);
  view.rerender(<ListCopyControl {...props} />);
  fireEvent.click(screen.getByRole('button', { name: 'Review List copy' }));
  await screen.findByText('Copy Planning as Planning copy to Delivery?');
  expect(screen.getByRole('button', { name: 'Confirm List copy' })).toBeEnabled();
  view.rerender(<ListCopyControl {...props} refreshing />);
  expect(screen.getByRole('button', { name: 'Confirm List copy' })).toBeDisabled();
  view.rerender(<ListCopyControl {...props} snapshot={{ ...snapshot, lists: [{ list: { ...list, version: 3 }, cards: [] }] }} />);
  expect(screen.getByRole('button', { name: 'Confirm List copy' })).toBeDisabled();
  expect(screen.getByText(/This copy review changed/)).toBeVisible();
  expect(fetch).toHaveBeenCalledTimes(2);
});

it('checks destination authority before submitting only the reviewed source version/name/destination/key', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(reply(directory)).mockResolvedValueOnce(reply(target)).mockResolvedValueOnce(reply(ack, 201));
  vi.stubGlobal('fetch', fetch); render(<ListCopyControl {...props} />); await review(); confirm();
  await screen.findByText('List copy acknowledged. Check its destination Board for the current copy.');
  expect(fetch.mock.calls.map(call => call[0])).toEqual([`/organizations/${org}/boards`, `/boards/${destination}`, `/lists/${sourceId}/copy`]);
  expect(fetch.mock.calls[2][1].method).toBe('POST');
  expect(JSON.parse(fetch.mock.calls[2][1].body)).toEqual({ destinationBoardId: destination, name: 'Reviewed copy', version: 2 });
  expect(fetch.mock.calls[2][1].headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
  expect(props.onRefresh).toHaveBeenCalledOnce();
  expect(screen.getAllByText('List copy acknowledged. Check its destination Board for the current copy.')).toHaveLength(1);
  await waitFor(() => expect(props.onReturnFocus).toHaveBeenCalledOnce());
});
it('retains the original key/body after response loss and source removal without allowing a replacement intent', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(reply(directory)).mockResolvedValueOnce(reply(target))
    .mockRejectedValueOnce(new Error('Lost response')).mockResolvedValueOnce(reply(ack, 201)); vi.stubGlobal('fetch', fetch);
  const view = render(<ListCopyControl {...props} />); await review(); confirm();
  await screen.findByRole('button', { name: 'Retry this List copy' });
  view.rerender(<ListCopyControl {...props} snapshot={{ ...snapshot, lists: [] }} />);
  expect(screen.queryByRole('button', { name: 'Cancel copy' })).not.toBeInTheDocument();
  expect(screen.queryByRole('textbox', { name: 'Copy name' })).not.toBeInTheDocument();
  expect(screen.getByText('Copy Planning as Reviewed copy to Delivery?')).toBeVisible();
  fireEvent.click(screen.getByRole('button', { name: 'Retry this List copy' }));
  await screen.findByText('List copy acknowledged. Check its destination Board for the current copy.');
  expect(fetch.mock.calls[3][1].body).toBe(fetch.mock.calls[2][1].body);
  expect(fetch.mock.calls[3][1].headers.get('Idempotency-Key')).toBe(fetch.mock.calls[2][1].headers.get('Idempotency-Key'));
});
it.each([
  ['id', sourceId], ['id', 'invalid'], ['organizationId', destination], ['boardId', board],
  ['name', 'Another name'], ['version', 2], ['lifecycleState', 'archived'], ['rank', 'invalid'],
])('treats a wrong %s acknowledgment as uncertain rather than claiming success', async (field, value) => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(reply(directory)).mockResolvedValueOnce(reply(target))
    .mockResolvedValueOnce(reply({ ...ack, [field]: value }, 201)));
  render(<ListCopyControl {...props} />); await review(); confirm();
  await screen.findByRole('button', { name: 'Retry this List copy' });
  expect(screen.queryByText('List copy acknowledged. Check its destination Board for the current copy.')).not.toBeInTheDocument();
});
it('preserves the name draft but blocks a stale source review until explicitly renewed', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(reply(directory)).mockResolvedValueOnce(reply(target)); vi.stubGlobal('fetch', fetch);
  const view = render(<ListCopyControl {...props} />); await review();
  view.rerender(<ListCopyControl {...props} snapshot={{ ...snapshot, lists: [{ list: { ...list, version: 3 }, cards: [] }] }} />);
  expect(screen.getByRole('button', { name: 'Confirm List copy' })).toBeDisabled();
  expect(screen.getByRole('textbox', { name: 'Copy name' })).toHaveValue('  Reviewed copy  ');
  fireEvent.click(screen.getByRole('button', { name: 'Use current List for copy review' }));
  expect(screen.getByRole('button', { name: 'Confirm List copy' })).toBeDisabled();
  expect(fetch).toHaveBeenCalledTimes(2);
});
it('rejects a read-only or cross-Organization destination without rendering a protected target name or posting', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(reply(directory)).mockResolvedValueOnce(reply({ ...target,
    board: { ...target.board, organizationId: board, name: 'Protected target title' }, access: { ...target.access, canEdit: false } }));
  vi.stubGlobal('fetch', fetch); render(<ListCopyControl {...props} />);
  fireEvent.click(screen.getByRole('button', { name: 'Copy list' }));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Reload copy destinations' })).toBeEnabled());
  select('List to copy', 'Planning'); select('Destination Board', 'Delivery');
  fireEvent.click(screen.getByRole('button', { name: 'Review List copy' }));
  await screen.findByText('The destination Board is unavailable for copying. Choose another Board.');
  expect(screen.queryByText('Protected target title')).not.toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Confirm List copy' })).toBeDisabled(); expect(fetch).toHaveBeenCalledTimes(2);
});
it('clears protected review and aborts a pending write on permission loss, ignoring the late acknowledgment', async () => {
  let resolve!: (response: Response) => void;
  const fetch = vi.fn().mockResolvedValueOnce(reply(directory)).mockResolvedValueOnce(reply(target))
    .mockImplementationOnce(() => new Promise<Response>(done => { resolve = done; })); vi.stubGlobal('fetch', fetch);
  const view = render(<ListCopyControl {...props} />); await review(); confirm();
  const signal = fetch.mock.calls[2][1].signal as AbortSignal;
  view.rerender(<ListCopyControl {...props} snapshot={{ ...snapshot, access: { ...snapshot.access, canEdit: false } }} />);
  expect(signal.aborted).toBe(true);
  await act(async () => { resolve(reply(ack, 201)); });
  expect(screen.queryByText('Copy Planning as Reviewed copy to Delivery?')).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Retry this List copy' })).not.toBeInTheDocument();
  expect(screen.queryByText('List copy acknowledged. Check its destination Board for the current copy.')).not.toBeInTheDocument();
  expect(props.onRefresh).not.toHaveBeenCalled();
});
it('keeps fixed conflict text and requires another review without displaying server details', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(reply(directory)).mockResolvedValueOnce(reply(target))
    .mockResolvedValueOnce(reply({ title: 'Private SQL failure' }, 409)));
  render(<ListCopyControl {...props} />); await review(); confirm();
  await screen.findByText('This copy could not be applied. Check the Board and review a current List.');
  expect(screen.queryByText('Private SQL failure')).not.toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Confirm List copy' })).toBeDisabled();
  expect(screen.queryByRole('button', { name: 'Retry this List copy' })).not.toBeInTheDocument();
});
it('bounds the full acknowledgment body and retains a retry after timeout', async () => {
  const hanging = { status: 201, json: () => new Promise(() => undefined) };
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(reply(directory)).mockResolvedValueOnce(reply(target)).mockResolvedValueOnce(hanging));
  render(<ListCopyControl {...props} />); await review(); vi.useFakeTimers(); confirm();
  await act(async () => { await vi.advanceTimersByTimeAsync(15_001); }); vi.useRealTimers();
  await screen.findByRole('button', { name: 'Retry this List copy' });
});
it('rejects malformed discovery and clears a denied discovery without allowing a copy', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(reply([{ ...directory[0], id: 'private input' }]))
    .mockResolvedValueOnce(reply({ title: 'Private denied detail' }, 404)));
  render(<ListCopyControl {...props} />); fireEvent.click(screen.getByRole('button', { name: 'Copy list' }));
  await screen.findByText('Copy destinations could not be loaded. Try loading them again.');
  fireEvent.click(screen.getByRole('button', { name: 'Reload copy destinations' }));
  await screen.findByText('This List copy action is unavailable.');
  expect(screen.queryByRole('button', { name: 'Copy list' })).not.toBeInTheDocument();
  expect(screen.queryByText('Private denied detail')).not.toBeInTheDocument();
});
